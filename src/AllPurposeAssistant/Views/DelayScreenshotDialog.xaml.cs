using System.Windows;

namespace AllPurposeAssistant.Views;

public partial class DelayScreenshotDialog : Window
{
    public int DelaySeconds { get; private set; }

    public DelayScreenshotDialog()
    {
        InitializeComponent();
    }

    private void ThreeSeconds_Click(object sender, RoutedEventArgs e)
    {
        DelaySeconds = 3;
        DialogResult = true;
    }

    private void FiveSeconds_Click(object sender, RoutedEventArgs e)
    {
        DelaySeconds = 5;
        DialogResult = true;
    }
}
