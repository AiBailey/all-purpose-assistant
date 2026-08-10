using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using AllPurposeAssistant.Models;
using Microsoft.Win32;

namespace AllPurposeAssistant.Views;

public partial class AddQuickActionDialog : Window
{
    private readonly QuickAction? _editingAction;
    public string NameText => NameBox.Text.Trim();
    public string TargetText => TargetBox.Text.Trim();
    public ActionType SelectedActionType => ActionTypeCombo.SelectedItem is ComboBoxItem { Tag: string type } ? type switch
    {
        "OpenUrl" => ActionType.OpenUrl,
        "OpenFolder" => ActionType.OpenFolder,
        _ => ActionType.OpenApp
    } : ActionType.OpenApp;

    public AddQuickActionDialog(QuickAction? editingAction = null)
    {
        _editingAction = editingAction;
        InitializeComponent();
        LoadBackground();
        if (_editingAction != null)
        {
            DialogTitleText.Text = "编辑快捷操作";
            DialogSubtitleText.Text = "修改此快捷操作的名称、类型或打开目标";
            ConfirmButton.Content = "保存";
            NameBox.Text = _editingAction.Name;
            ActionTypeCombo.SelectedIndex = _editingAction.Type switch
            {
                ActionType.OpenUrl => 1,
                ActionType.OpenFolder => 2,
                _ => 0
            };
            TargetBox.Text = _editingAction.Target;
        }
        UpdateTargetPresentation();
        Loaded += (_, _) => NameBox.Focus();
    }

    private void LoadBackground()
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
            BackgroundImage.Source = image;
        }
        catch
        {
        }
    }

    private void ActionType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateTargetPresentation();
    }

    private void UpdateTargetPresentation()
    {
        if (TargetLabel == null || TargetHint == null || BrowseButton == null) return;
        switch (SelectedActionType)
        {
            case ActionType.OpenUrl:
                TargetLabel.Text = "网页地址";
                TargetHint.Text = "请输入以 https:// 或 http:// 开头的网址";
                BrowseButton.Visibility = Visibility.Collapsed;
                break;
            case ActionType.OpenFolder:
                TargetLabel.Text = "文件夹路径";
                TargetHint.Text = "点击右侧按钮选择要打开的文件夹";
                BrowseButton.Visibility = Visibility.Visible;
                break;
            default:
                TargetLabel.Text = "软件路径";
                TargetHint.Text = "可填 exe 路径，或点击右侧按钮选择文件";
                BrowseButton.Visibility = Visibility.Visible;
                break;
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedActionType == ActionType.OpenFolder)
        {
            var folderDialog = new OpenFolderDialog
            {
                InitialDirectory = Directory.Exists(TargetBox.Text) ? TargetBox.Text : ""
            };
            if (folderDialog.ShowDialog(this) == true)
                TargetBox.Text = folderDialog.FolderName;
            return;
        }

        var dlg = new OpenFileDialog
        {
            Filter = "程序(*.exe)|*.exe|可执行(所有)|*.exe;*.url;*.lnk",
            Title = "选择要打开的软件"
        };
        if (dlg.ShowDialog(this) == true)
            TargetBox.Text = dlg.FileName;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show(this, "请输入名称", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            NameBox.Focus();
            return;
        }
        if (string.IsNullOrWhiteSpace(TargetBox.Text))
        {
            var message = SelectedActionType switch
            {
                ActionType.OpenUrl => "请输入网页地址",
                ActionType.OpenFolder => "请输入或选择文件夹路径",
                _ => "请输入或选择软件路径"
            };
            MessageBox.Show(this, message,
                "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            TargetBox.Focus();
            return;
        }
        if (SelectedActionType == ActionType.OpenUrl
            && (!Uri.TryCreate(TargetText, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")))
        {
            MessageBox.Show(this, "请输入有效的 http 或 https 网页地址", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            TargetBox.Focus();
            return;
        }
        if (SelectedActionType == ActionType.OpenFolder && !Directory.Exists(TargetText))
        {
            MessageBox.Show(this, "请选择存在的文件夹", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            TargetBox.Focus();
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!CloseButton.IsMouseOver && e.ClickCount == 1) DragMove();
    }
}
