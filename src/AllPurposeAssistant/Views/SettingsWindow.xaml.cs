using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using AllPurposeAssistant.Services;
using Microsoft.Win32;

namespace AllPurposeAssistant.Views;

public partial class SettingsWindow : Window
{
    private readonly WindowManager _windowManager;
    private string _previousHotKey = "";

    public SettingsWindow(WindowManager windowManager)
    {
        _windowManager = windowManager;
        InitializeComponent();
        LoadBackground();
        HotKeyText.Text = _windowManager.ScreenshotHotKey;
        _previousHotKey = HotKeyText.Text;
        SaveDirectoryText.Text = _windowManager.ScreenshotSaveDirectory ?? "";
        SaveFormatCombo.SelectedIndex = _windowManager.ScreenshotSaveFormat == "Jpeg" ? 1 : 0;
        JpegQualityText.Text = _windowManager.ScreenshotJpegQuality.ToString(CultureInfo.InvariantCulture);
        PinnedOpacityCombo.SelectedIndex = _windowManager.PinnedScreenshotOpacity switch
        {
            <= 0.7 => 2,
            <= 0.9 => 1,
            _ => 0
        };
        SelectSettingsTab(showScreenshot: false);
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

    private void HotKeyText_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _previousHotKey = HotKeyText.Text;
        HotKeyText.SelectAll();
    }

    private void HotKeyText_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            HotKeyText.Text = _previousHotKey;
            Keyboard.ClearFocus();
            e.Handled = true;
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift)
        {
            e.Handled = true;
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None || key == Key.None)
        {
            e.Handled = true;
            return;
        }

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        parts.Add(key.ToString());
        HotKeyText.Text = string.Join('+', parts);
        HotKeyText.SelectAll();
        e.Handled = true;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            InitialDirectory = Directory.Exists(SaveDirectoryText.Text) ? SaveDirectoryText.Text : ""
        };
        if (dialog.ShowDialog() == true)
            SaveDirectoryText.Text = dialog.FolderName;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void StartupSetup_Click(object sender, RoutedEventArgs e)
    {
        _windowManager.ShowStartupSetup();
    }

    private void GeneralTab_Click(object sender, RoutedEventArgs e)
    {
        SelectSettingsTab(showScreenshot: false);
    }

    private void ScreenshotTab_Click(object sender, RoutedEventArgs e)
    {
        SelectSettingsTab(showScreenshot: true);
    }

    private void SelectSettingsTab(bool showScreenshot)
    {
        GeneralSettingsPanel.Visibility = showScreenshot ? Visibility.Collapsed : Visibility.Visible;
        ScreenshotSettingsPanel.Visibility = showScreenshot ? Visibility.Visible : Visibility.Collapsed;
        ScreenshotSaveButton.Visibility = showScreenshot ? Visibility.Visible : Visibility.Collapsed;
        GeneralTabButton.Background = showScreenshot ? System.Windows.Media.Brushes.Transparent : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(191, 255, 255, 255));
        ScreenshotTabButton.Background = showScreenshot ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(191, 255, 255, 255)) : System.Windows.Media.Brushes.Transparent;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (SettingsCloseButton.IsMouseOver || e.ClickCount != 1) return;
        DragMove();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(JpegQualityText.Text, out var jpegQuality)
            || SaveFormatCombo.SelectedItem is not ComboBoxItem formatItem
            || PinnedOpacityCombo.SelectedItem is not ComboBoxItem opacityItem
            || formatItem.Tag is not string format
            || opacityItem.Tag is not string opacityText
            || !double.TryParse(opacityText, CultureInfo.InvariantCulture, out var opacity))
        {
            MessageBox.Show("请检查 JPEG 质量和钉图透明度。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!_windowManager.TryApplyScreenshotSettings(HotKeyText.Text, SaveDirectoryText.Text.Trim(),
                format, jpegQuality, opacity, out var error))
        {
            MessageBox.Show(error, "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}
