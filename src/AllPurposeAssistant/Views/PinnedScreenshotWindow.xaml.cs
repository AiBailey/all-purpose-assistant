using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace AllPurposeAssistant.Views;

public partial class PinnedScreenshotWindow : Window
{
    private const double MinWidth = 120;
    private const double MinHeight = 80;
    private readonly BitmapSource _image;

    public PinnedScreenshotWindow(BitmapSource image, double opacity = 1)
    {
        _image = image;
        InitializeComponent();
        PinnedImage.Source = image;
        Opacity = Math.Clamp(opacity, 0.2, 1);
        Opacity100MenuItem.IsChecked = Opacity == 1;
        Opacity80MenuItem.IsChecked = Opacity == 0.8;
        Opacity60MenuItem.IsChecked = Opacity == 0.6;

        const double maxWidth = 800;
        const double maxHeight = 600;
        var scale = Math.Min(1, Math.Min(maxWidth / image.PixelWidth, maxHeight / image.PixelHeight));
        Width = Math.Max(MinWidth, image.PixelWidth * scale);
        Height = Math.Max(MinHeight, image.PixelHeight * scale);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Source is Button) return;
        DragMove();
    }

    private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scale = e.Delta > 0 ? 1.1 : 1 / 1.1;
        var maxWidth = SystemParameters.WorkArea.Width * 0.9;
        var maxHeight = SystemParameters.WorkArea.Height * 0.9;
        var nextWidth = Math.Clamp(Width * scale, MinWidth, maxWidth);
        var nextHeight = Math.Clamp(Height * scale, MinHeight, maxHeight);
        var actualScale = Math.Min(nextWidth / Width, nextHeight / Height);
        Width *= actualScale;
        Height *= actualScale;
        e.Handled = true;
    }

    private void Content_MouseEnter(object sender, MouseEventArgs e)
    {
        CloseButton.Visibility = Visibility.Visible;
    }

    private void Content_MouseLeave(object sender, MouseEventArgs e)
    {
        CloseButton.Visibility = Visibility.Collapsed;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void CopyImage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetImage(_image);
        }
        catch
        {
        }
    }

    private void Opacity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not string value
            || !double.TryParse(value, out var opacity))
            return;

        Opacity = opacity;
        Opacity100MenuItem.IsChecked = Opacity == 1;
        Opacity80MenuItem.IsChecked = Opacity == 0.8;
        Opacity60MenuItem.IsChecked = Opacity == 0.6;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
}
