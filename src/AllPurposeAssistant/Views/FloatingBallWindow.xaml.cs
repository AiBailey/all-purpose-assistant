using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AllPurposeAssistant.Helpers;
using AllPurposeAssistant.Models;
using AllPurposeAssistant.Services;

namespace AllPurposeAssistant.Views;

public partial class FloatingBallWindow : Window
{
    private readonly WindowManager _windowManager;
    private readonly NoteService _noteService;
    private readonly QuickActionsService _quickActionsService;
    private readonly ActionExecutor _actionExecutor = new();
    private Point _dragStart;
    private bool _isDragging;

    private readonly DispatcherTimer _metricsRefresh = new() { Interval = TimeSpan.FromSeconds(1) };

    public FloatingBallWindow(WindowManager windowManager, NoteService noteService,
        QuickActionsService quickActionsService)
    {
        _windowManager = windowManager;
        _noteService = noteService;
        _quickActionsService = quickActionsService;
        InitializeComponent();

        _metricsRefresh.Tick += (_, _) => UpdateMetrics();

        RefreshQuickActions();
        _quickActionsService.Changed += OnQuickActionsChanged;

        Loaded += (_, _) =>
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            NativeMethods.SetToolWindow(helper.Handle);
            LoadArtwork();
            // 建立 CPU 采样基线，让首次悬停即显示有参考意义的占用率。
            SystemMetrics.GetCpuUsagePercent();
        };

        // 窗口被 Hide（如最小化到托盘）时不一定触发 MouseLeave，这里兜底清理状态。
        IsVisibleChanged += (_, e) =>
        {
            if (!IsVisible)
                ResetBall();
        };

        Closing += (_, e) =>
        {
            _quickActionsService.Changed -= OnQuickActionsChanged;
            e.Cancel = true;
            Hide();
        };
    }

    private void OnQuickActionsChanged()
    {
        Dispatcher.BeginInvoke(RefreshQuickActions);
    }

    private void RefreshQuickActions()
    {
        if (QuickActionsPanel == null) return;
        QuickActionsPanel.Children.Clear();

        foreach (var action in _quickActionsService.All)
        {
            var btn = new Button
            {
                Content = action.Icon + " " + action.Name,
                FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei"),
                Tag = action,
                Cursor = Cursors.Hand
            };
            btn.Style = (Style)Application.Current.Resources["SidebarButton"];
            btn.Click += QuickAction_Click;
            QuickActionsPanel.Children.Add(btn);
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _isDragging = false;
        CaptureMouse();
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        if (!_isDragging)
            ActionPanel.IsOpen = !ActionPanel.IsOpen;
    }

    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        BallEllipse.Width = 60;
        BallEllipse.Height = 60;
        BallLabel.Visibility = Visibility.Collapsed;
        MetricsOverlay.Visibility = Visibility.Visible;
        UpdateMetrics();
        _metricsRefresh.Start();
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        ResetBall();
    }

    private void ResetBall()
    {
        BallEllipse.Width = 56;
        BallEllipse.Height = 56;
        BallLabel.Visibility = Visibility.Visible;
        MetricsOverlay.Visibility = Visibility.Collapsed;
        _metricsRefresh.Stop();
    }

    private void UpdateMetrics()
    {
        CpuText.Text = $"CPU {SystemMetrics.GetCpuUsagePercent():0}%";
        MemText.Text = $"内存 {SystemMetrics.GetMemoryUsagePercent():0}%";
    }

    private void QuickAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is QuickAction action)
            _actionExecutor.Execute(action);
        ActionPanel.IsOpen = false;
    }

    private void NewNote_Click(object sender, RoutedEventArgs e)
    {
        ActionPanel.IsOpen = false;
        var note = _noteService.GetOrCreateDefault();
        var editor = new NoteEditorWindow(_noteService, note, new Point(Left, Top));
        editor.Show();
    }

    private void Screenshot_Click(object sender, RoutedEventArgs e)
    {
        ActionPanel.IsOpen = false;
        _windowManager.StartScreenshot();
    }

    private void LoadArtwork()
    {
        try
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bg_sidebar.png");
            if (!File.Exists(path)) return;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            BallArtwork.Fill = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        }
        catch
        {
        }
    }

    private void DelayedScreenshot_Click(object sender, RoutedEventArgs e)
    {
        ActionPanel.IsOpen = false;
        _windowManager.ShowDelayScreenshotDialog();
    }

    private void SwitchToSidebar_Click(object sender, RoutedEventArgs e)
    {
        _windowManager.SwitchMode(UiMode.Sidebar);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        _windowManager.ShowSettings();
    }

    private void MinimizeToTray_Click(object sender, RoutedEventArgs e)
    {
        _windowManager.MinimizeToTray();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.LeftButton == MouseButtonState.Pressed && IsMouseCaptured)
        {
            var currentPos = e.GetPosition(this);
            var diff = currentPos - _dragStart;
            if (Math.Abs(diff.X) > 3 || Math.Abs(diff.Y) > 3)
                _isDragging = true;
            if (_isDragging)
            {
                Left += diff.X;
                Top += diff.Y;
            }
        }
    }
}
