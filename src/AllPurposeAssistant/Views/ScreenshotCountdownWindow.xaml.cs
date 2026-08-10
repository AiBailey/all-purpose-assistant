using System.Windows;
using System.Windows.Input;

namespace AllPurposeAssistant.Views;

public partial class ScreenshotCountdownWindow : Window
{
    private bool _completed;

    public event Action? Cancelled;

    public ScreenshotCountdownWindow()
    {
        InitializeComponent();
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 24;
        Top = workArea.Bottom - Height - 24;
    }

    public void SetRemainingSeconds(int seconds)
    {
        CountdownText.Text = $"{seconds} 秒后截图";
    }

    public void Complete()
    {
        _completed = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (!_completed)
            Cancelled?.Invoke();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
}
