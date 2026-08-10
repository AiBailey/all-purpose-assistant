using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AllPurposeAssistant.Helpers;
using AllPurposeAssistant.Models;
using AllPurposeAssistant.Services;

namespace AllPurposeAssistant.Views;

public partial class SidebarWindow : Window
{
    private const double DockRevealMargin = 5;
    private const double DockingThreshold = 40;
    private const double ExpandGap = 14;

    private readonly WindowManager _windowManager;
    private readonly ActionExecutor _actionExecutor;
    private readonly QuickActionsService _quickActionsService;
    private readonly ClipboardService _clipboardService;
    private readonly DispatcherTimer _dockTimer;
    private double _screenRight;
    private double _expandedLeft;
    private double _dockedLeft;
    private bool _docked;
    private bool _snapEnabled;
    private Button? _quickActionDragSource;
    private Point _quickActionDragStart;
    private bool _suppressQuickActionClick;

    public SidebarWindow(WindowManager windowManager, ClipboardService clipboardService,
        QuickActionsService quickActionsService)
    {
        _windowManager = windowManager;
        _clipboardService = clipboardService;
        _quickActionsService = quickActionsService;
        _actionExecutor = new ActionExecutor();
        InitializeComponent();

        var workArea = SystemParameters.WorkArea;
        _screenRight = workArea.Right;
        Height = workArea.Height * 0.8;
        Top = workArea.Top + (workArea.Height * 0.1);

        _expandedLeft = workArea.Right - Width - ExpandGap;
        _dockedLeft = workArea.Right - DockRevealMargin;
        Left = _expandedLeft;

        _dockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _dockTimer.Tick += DockTick;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
                _dockTimer.Start();
            else
                _dockTimer.Stop();
        };

        RefreshClipboardList();
        RefreshQuickActions();
        _clipboardService.Changed += OnClipboardChanged;
        _quickActionsService.Changed += OnQuickActionsChanged;

        Loaded += (_, _) =>
        {
            var helper = new WindowInteropHelper(this);
            NativeMethods.SetToolWindow(helper.Handle);
            LoadBackground();
            _snapEnabled = true;
        };

        Closing += (_, e) =>
        {
            _dockTimer.Stop();
            _clipboardService.Changed -= OnClipboardChanged;
            _quickActionsService.Changed -= OnQuickActionsChanged;
            e.Cancel = true;
            Hide();
        };
    }

    private void OnQuickActionsChanged()
    {
        Dispatcher.BeginInvoke(RefreshQuickActions);
    }

    private void OnClipboardChanged()
    {
        Dispatcher.BeginInvoke(RefreshClipboardList);
    }

    private void RefreshClipboardList()
    {
        if (ClipboardList == null) return;
        ClipboardList.ItemsSource = _clipboardService.Entries;
        UpdateClipboardCount();
    }

    private void UpdateClipboardCount()
    {
        if (ClipboardCountText == null) return;
        ClipboardCountText.Text = $"{_clipboardService.Entries.Count} 条";
    }

    private void RefreshQuickActions()
    {
        if (QuickActionsGrid == null) return;
        QuickActionsGrid.Children.Clear();

        foreach (var action in _quickActionsService.All)
        {
            var btn = CreateQuickActionButton(action);
            QuickActionsGrid.Children.Add(btn);
        }
    }

    private Button CreateQuickActionButton(QuickAction action)
    {
        var btn = new Button
        {
            FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei"),
            Tag = action,
            Cursor = Cursors.Hand,
            ContextMenu = null,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        btn.Style = (Style)Application.Current.Resources["SidebarButton"];
        btn.Margin = new Thickness(6, 3, 2, 3);
        btn.HorizontalAlignment = HorizontalAlignment.Stretch;
        btn.Click += QuickAction_Click;

        if (action.IsFixed)
        {
            btn.Content = action.Icon + " " + action.Name;
            return btn;
        }

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(new TextBlock
        {
            Text = action.Icon + " " + action.Name,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var actionButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var editButton = new Button
        {
            Content = "✎",
            Style = (Style)FindResource("QuickActionEditButtonStyle"),
            ToolTip = "编辑"
        };
        editButton.Click += (_, e) =>
        {
            e.Handled = true;
            EditQuickAction(action);
        };
        actionButtons.Children.Add(editButton);

        var removeButton = new Button
        {
            Content = "×",
            Style = (Style)FindResource("QuickActionRemoveButtonStyle"),
            ToolTip = "删除"
        };
        removeButton.Click += (_, e) =>
        {
            e.Handled = true;
            _quickActionsService.Remove(action);
        };
        actionButtons.Children.Add(removeButton);
        Grid.SetColumn(actionButtons, 1);
        content.Children.Add(actionButtons);
        btn.Content = content;
        btn.AllowDrop = true;
        btn.PreviewMouseLeftButtonDown += CustomAction_PreviewMouseLeftButtonDown;
        btn.PreviewMouseMove += CustomAction_PreviewMouseMove;
        btn.PreviewMouseLeftButtonUp += CustomAction_PreviewMouseLeftButtonUp;
        btn.DragOver += CustomAction_DragOver;
        btn.DragLeave += CustomAction_DragLeave;
        btn.Drop += CustomAction_Drop;
        return btn;
    }

    private void CustomAction_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button || FindContainingButton(e.OriginalSource as DependencyObject) != button) return;
        _quickActionDragSource = button;
        _quickActionDragStart = e.GetPosition(button);
    }

    private void CustomAction_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_quickActionDragSource is not { Tag: QuickAction action } source || e.LeftButton != MouseButtonState.Pressed)
            return;

        var currentPosition = e.GetPosition(source);
        if (Math.Abs(currentPosition.X - _quickActionDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(currentPosition.Y - _quickActionDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _suppressQuickActionClick = true;
        DragDrop.DoDragDrop(source, action, DragDropEffects.Move);
        _quickActionDragSource = null;
        Dispatcher.BeginInvoke(() => _suppressQuickActionClick = false, DispatcherPriority.Background);
    }

    private void CustomAction_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _quickActionDragSource = null;
    }

    private void CustomAction_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not Button target || target.Tag is not QuickAction targetAction
            || e.Data.GetData(typeof(QuickAction)) is not QuickAction sourceAction
            || ReferenceEquals(sourceAction, targetAction))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        target.Background = new SolidColorBrush(Color.FromArgb(128, 207, 219, 255));
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void CustomAction_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Button button)
            button.Background = Brushes.Transparent;
    }

    private void CustomAction_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Button target || target.Tag is not QuickAction targetAction
            || e.Data.GetData(typeof(QuickAction)) is not QuickAction sourceAction)
            return;

        target.Background = Brushes.Transparent;
        var placeAfter = e.GetPosition(target).X > target.ActualWidth / 2;
        _quickActionsService.Move(sourceAction, targetAction, placeAfter);
        e.Handled = true;
    }

    private static Button? FindContainingButton(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is Button button) return button;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    private void AddQuickAction_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddQuickActionDialog { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _quickActionsService.Add(dialog.NameText, dialog.TargetText, dialog.SelectedActionType);
        }
    }

    private void EditQuickAction(QuickAction action)
    {
        var dialog = new AddQuickActionDialog(action) { Owner = this };
        if (dialog.ShowDialog() == true)
            _quickActionsService.Update(action, dialog.NameText, dialog.TargetText, dialog.SelectedActionType);
    }

    private void LoadBackground()
    {
        try
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bg_sidebar.png");
            if (!File.Exists(path)) return;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            BgImage.Source = bmp;
        }
        catch
        {
        }
    }

    private void DockTick(object? sender, EventArgs e)
    {
        if (!IsVisible) return;
        if (!_snapEnabled) return;

        var cursor = NativeMethods.GetCursorPos();

        if (_docked)
        {
            if (cursor.X >= Left + DockRevealMargin - 8)
                SlideTo(_expandedLeft, false);
        }
        else
        {
            if (cursor.X < Left - 4)
                SlideTo(_dockedLeft, true);
        }
    }

    private void SlideTo(double targetLeft, bool docked)
    {
        if (_docked == docked) return;

        var anim = new DoubleAnimation(Left, targetLeft, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(LeftProperty, anim);
        _docked = docked;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (HeaderMenuButton.IsMouseOver) return;
        if (e.ClickCount == 1)
        {
            BeginAnimation(LeftProperty, null);
            _snapEnabled = false;
            DragMove();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (Left + Width >= _screenRight - DockingThreshold)
        {
            _snapEnabled = true;
            _dockedLeft = _screenRight - DockRevealMargin;
            _expandedLeft = _screenRight - Width - ExpandGap;
            _docked = true;
            Left = _dockedLeft;
        }
        else
        {
            _snapEnabled = false;
            _docked = false;
        }
    }

    private void QuickAction_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressQuickActionClick) return;
        if (sender is Button btn && btn.Tag is QuickAction action)
            _actionExecutor.Execute(action);
    }

    private void NewNote_Click(object sender, RoutedEventArgs e)
    {
        _windowManager.ShowNoteEditor(new Point(Left, Top));
    }

    private void Screenshot_Click(object sender, RoutedEventArgs e)
    {
        _windowManager.StartScreenshot();
    }

    private void DelayedScreenshot_Click(object sender, RoutedEventArgs e)
    {
        _windowManager.ShowDelayScreenshotDialog();
    }

    private void ClipboardItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (ClipboardList.SelectedItem is ClipboardEntry entry)
            _clipboardService.CopyToClipboard(entry);
    }

    private void ClearClipboard_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(this, "确定要清空全部剪贴板历史吗？\n（历史中的图片文件将被删除）",
            "清空剪贴板历史", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return;
        _clipboardService.Clear();
    }

    private void SwitchToBall_Click(object sender, RoutedEventArgs e)
    {
        _windowManager.SwitchMode(UiMode.FloatingBall);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        _windowManager.ShowSettings();
    }

    private void HeaderMenu_Click(object sender, RoutedEventArgs e)
    {
        SidebarContextMenu.PlacementTarget = HeaderMenuButton;
        SidebarContextMenu.Placement = PlacementMode.Bottom;
        SidebarContextMenu.IsOpen = true;
    }

    private void MinimizeToTray_Click(object sender, RoutedEventArgs e)
    {
        _windowManager.MinimizeToTray();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }
}
