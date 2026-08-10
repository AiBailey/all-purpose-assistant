using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace AllPurposeAssistant.Views;

public partial class FirstRunWindow : Window
{
    public bool CreateDesktopShortcut => DesktopShortcutCheckBox.IsChecked == true;
    public bool LaunchAtLogin => LaunchAtLoginCheckBox.IsChecked == true;

    public FirstRunWindow(bool launchAtLogin = false)
    {
        InitializeComponent();
        LaunchAtLoginCheckBox.IsChecked = launchAtLogin;
        LoadBackground();
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

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
