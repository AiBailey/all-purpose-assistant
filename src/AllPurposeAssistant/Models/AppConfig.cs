namespace AllPurposeAssistant.Models;

public enum UiMode
{
    FloatingBall,
    Sidebar
}

public class AppConfig
{
    public UiMode Mode { get; set; } = UiMode.FloatingBall;
    public double FloatingBallX { get; set; }
    public double FloatingBallY { get; set; }
    public double SidebarWidth { get; set; } = 300;
    public bool Topmost { get; set; } = true;
    public bool FirstRun { get; set; } = true;
    public string ScreenshotHotKey { get; set; } = "Ctrl+Alt+Shift+Z";
    public string? ScreenshotSaveDirectory { get; set; }
    public string ScreenshotSaveFormat { get; set; } = "Png";
    public int ScreenshotJpegQuality { get; set; } = 92;
    public double PinnedScreenshotOpacity { get; set; } = 1;
}
