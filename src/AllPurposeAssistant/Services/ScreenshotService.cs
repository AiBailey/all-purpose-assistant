using System.Windows;
using System.Windows.Media.Imaging;
using AllPurposeAssistant.Helpers;
using AllPurposeAssistant.Views;

namespace AllPurposeAssistant.Services;

public class ScreenshotService
{
    private bool _capturing;
    private string? _defaultSaveDirectory;
    private string _defaultSaveFormat = "Png";
    private int _jpegQuality = 92;
    private double _pinnedOpacity = 1;

    public void UpdateSettings(string? defaultSaveDirectory, string? defaultSaveFormat,
        int jpegQuality, double pinnedOpacity)
    {
        _defaultSaveDirectory = defaultSaveDirectory;
        _defaultSaveFormat = defaultSaveFormat is "Jpeg" ? "Jpeg" : "Png";
        _jpegQuality = Math.Clamp(jpegQuality, 1, 100);
        _pinnedOpacity = Math.Clamp(pinnedOpacity, 0.2, 1);
    }

    // 触发区域截图：先截全屏，再开遮罩窗口拖选
    public void Start()
    {
        if (_capturing) return;
        _capturing = true;
        BeginCapture();
    }

    public void StartDelayed(int seconds)
    {
        if (_capturing || seconds <= 0) return;
        _capturing = true;
        _ = RunDelayedCapture(seconds);
    }

    private async Task RunDelayedCapture(int seconds)
    {
        using var cancellation = new CancellationTokenSource();
        var countdown = new ScreenshotCountdownWindow();
        countdown.Cancelled += cancellation.Cancel;
        var beganCapture = false;
        try
        {
            countdown.Show();
            for (var remaining = seconds; remaining > 0; remaining--)
            {
                countdown.SetRemainingSeconds(remaining);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellation.Token);
            }

            cancellation.Token.ThrowIfCancellationRequested();
            countdown.Complete();
            await Task.Delay(150);
            cancellation.Token.ThrowIfCancellationRequested();
            BeginCapture();
            beganCapture = true;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            countdown.Cancelled -= cancellation.Cancel;
            if (!beganCapture)
                _capturing = false;
        }
    }

    private void BeginCapture()
    {
        BitmapSource? fullScreen = null;
        try
        {
            fullScreen = ScreenCapture.CaptureVirtualScreen(out var captureBounds);
            var overlay = new ScreenshotOverlayWindow(
                captureBounds,
                fullScreen,
                ScreenCapture.GetDpiScale(),
                OnCancel);
            overlay.ConfigureInlineEditor(_defaultSaveDirectory, _defaultSaveFormat, _jpegQuality, _pinnedOpacity);
            overlay.Closed += (_, _) => _capturing = false;
            overlay.Show(); // 非模态
        }
        catch
        {
            _capturing = false;
        }
    }

    private void OnCancel()
    {
        _capturing = false;
    }
}
