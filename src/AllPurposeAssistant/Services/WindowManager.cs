using System.IO;
using System.Drawing;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using AllPurposeAssistant.Helpers;
using AllPurposeAssistant.Models;
using AllPurposeAssistant.Views;
using H.NotifyIcon;

namespace AllPurposeAssistant.Services;

public class WindowManager
{
    private readonly PersistenceService _persistence;
    private readonly NoteService _noteService;
    private readonly ClipboardService _clipboardService;
    private readonly QuickActionsService _quickActionsService;
    private readonly ScreenshotService _screenshotService;
    private readonly HotKeyManager _hotKeyManager;
    private readonly StartupService _startupService;
    private AppConfig _config;

    private FloatingBallWindow? _floatingBall;
    private SidebarWindow? _sidebar;
    private TaskbarIcon? _trayIcon;
    private HwndSource? _hotKeyHost;
    private int _screenshotHotKeyId;

    public event Action<UiMode>? ModeChanged;

    public WindowManager(PersistenceService persistence, NoteService noteService,
        ClipboardService clipboardService, QuickActionsService quickActionsService,
        ScreenshotService screenshotService, HotKeyManager hotKeyManager, StartupService startupService)
    {
        _persistence = persistence;
        _noteService = noteService;
        _clipboardService = clipboardService;
        _quickActionsService = quickActionsService;
        _screenshotService = screenshotService;
        _hotKeyManager = hotKeyManager;
        _startupService = startupService;
        _config = persistence.Load<AppConfig>("config.json") ?? new AppConfig();
    }

    public void Initialize()
    {
        try
        {
            ApplyScreenshotSettings();
            var isFirstRun = _config.FirstRun;
            if (_config.Mode == UiMode.FloatingBall)
            {
                PositionBallTopRight();
            }

            if (isFirstRun)
            {
                _config.FirstRun = false;
                SaveState();
            }

            if (_config.Mode == UiMode.FloatingBall)
                ShowFloatingBall();
            else
                ShowSidebar();

            // 延迟创建托盘图标 + 注册全局热键，等待消息循环就绪
            Application.Current.Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle, new Action(SetupGlobalHotKey));
            Application.Current.Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle, new Action(CreateTrayIcon));
            if (isFirstRun)
            {
                Application.Current.Dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle, new Action(ShowStartupSetup));
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"初始化失败: {ex}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetupGlobalHotKey()
    {
        try
        {
            var parameters = new HwndSourceParameters("HotKeyHost")
            {
                Width = 0, Height = 0, WindowStyle = 0
            };
            _hotKeyHost = new HwndSource(parameters);
            _hotKeyManager.Initialize(_hotKeyHost.Handle);
            if (!TryParseHotKey(_config.ScreenshotHotKey, out var modifiers, out var key, out var normalized))
            {
                TryParseHotKey("Ctrl+Alt+Shift+Z", out modifiers, out key, out normalized);
                _config.ScreenshotHotKey = normalized;
            }
            _screenshotHotKeyId = _hotKeyManager.Register(modifiers, key,
                () => Application.Current.Dispatcher.BeginInvoke(StartScreenshot));
        }
        catch
        {
        }
    }

    public void StartScreenshot()
    {
        _screenshotService.Start();
    }

    public void ShowNoteEditor(System.Windows.Point anchorPoint)
    {
        var note = _noteService.GetOrCreateDefault();
        new NoteEditorWindow(_noteService, note, anchorPoint).Show();
    }

    public string ScreenshotHotKey => _config.ScreenshotHotKey;
    public string? ScreenshotSaveDirectory => _config.ScreenshotSaveDirectory;
    public string ScreenshotSaveFormat => _config.ScreenshotSaveFormat;
    public int ScreenshotJpegQuality => _config.ScreenshotJpegQuality;
    public double PinnedScreenshotOpacity => _config.PinnedScreenshotOpacity;

    public void ShowSettings()
    {
        new SettingsWindow(this).ShowDialog();
    }

    public void ShowStartupSetup()
    {
        var setup = new FirstRunWindow(_startupService.IsLaunchAtLogin());
        if (setup.ShowDialog() != true) return;

        try
        {
            _startupService.SetLaunchAtLogin(setup.LaunchAtLogin);
            if (setup.CreateDesktopShortcut && !_startupService.TryCreateDesktopShortcut(out var error))
            {
                MessageBox.Show($"创建桌面快捷方式失败：{error}", "小帮手",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存启动设置失败：{ex.Message}", "小帮手",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void ShowDelayScreenshotDialog()
    {
        var dialog = new DelayScreenshotDialog();
        if (dialog.ShowDialog() == true)
            _screenshotService.StartDelayed(dialog.DelaySeconds);
    }

    public bool TryApplyScreenshotSettings(string hotKey, string? saveDirectory,
        string saveFormat, int jpegQuality, double pinnedOpacity, out string error)
    {
        error = "";
        if (!TryParseHotKey(hotKey, out var modifiers, out var key, out var normalized))
        {
            error = "快捷键格式不正确，请使用 Ctrl+Alt+Shift+Z 这类格式。";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(saveDirectory) && !Directory.Exists(saveDirectory))
        {
            error = "默认保存目录不存在。";
            return false;
        }

        if (jpegQuality is < 1 or > 100)
        {
            error = "JPEG 质量请输入 1 到 100。";
            return false;
        }

        if (pinnedOpacity is < 0.2 or > 1)
        {
            error = "钉图不透明度必须在 20% 到 100% 之间。";
            return false;
        }

        if (!string.Equals(normalized, _config.ScreenshotHotKey, StringComparison.OrdinalIgnoreCase)
            && _hotKeyHost != null)
        {
            var previousHotKey = _config.ScreenshotHotKey;
            if (_screenshotHotKeyId != 0)
                _hotKeyManager.Unregister(_screenshotHotKeyId);

            _screenshotHotKeyId = _hotKeyManager.Register(modifiers, key,
                () => Application.Current.Dispatcher.BeginInvoke(StartScreenshot));
            if (_screenshotHotKeyId == 0)
            {
                if (TryParseHotKey(previousHotKey, out var previousModifiers, out var previousKey, out _))
                    _screenshotHotKeyId = _hotKeyManager.Register(previousModifiers, previousKey,
                        () => Application.Current.Dispatcher.BeginInvoke(StartScreenshot));
                error = "该快捷键已被其他程序占用。";
                return false;
            }
        }

        _config.ScreenshotHotKey = normalized;
        _config.ScreenshotSaveDirectory = string.IsNullOrWhiteSpace(saveDirectory) ? null : saveDirectory;
        _config.ScreenshotSaveFormat = saveFormat == "Jpeg" ? "Jpeg" : "Png";
        _config.ScreenshotJpegQuality = jpegQuality;
        _config.PinnedScreenshotOpacity = pinnedOpacity;
        ApplyScreenshotSettings();
        SaveState();
        return true;
    }

    private void ApplyScreenshotSettings()
    {
        _screenshotService.UpdateSettings(_config.ScreenshotSaveDirectory, _config.ScreenshotSaveFormat,
            _config.ScreenshotJpegQuality, _config.PinnedScreenshotOpacity);
    }

    private static bool TryParseHotKey(string? value, out int modifiers, out int key, out string normalized)
    {
        modifiers = 0;
        key = 0;
        normalized = "";
        if (string.IsNullOrWhiteSpace(value)) return false;

        string? keyName = null;
        foreach (var part in value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= NativeMethods.MOD_CONTROL;
                    break;
                case "ALT":
                    modifiers |= NativeMethods.MOD_ALT;
                    break;
                case "SHIFT":
                    modifiers |= NativeMethods.MOD_SHIFT;
                    break;
                default:
                    if (keyName != null) return false;
                    keyName = part;
                    break;
            }
        }

        if (keyName == null || modifiers == 0) return false;
        var converted = new KeyConverter().ConvertFromString(keyName);
        if (converted is not Key parsedKey || parsedKey == Key.None) return false;
        key = KeyInterop.VirtualKeyFromKey(parsedKey);
        if (key == 0) return false;

        var parts = new List<string>();
        if ((modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        parts.Add(parsedKey.ToString());
        normalized = string.Join('+', parts);
        return true;
    }

    private void CreateTrayIcon()
    {
        try
        {
            var menu = new System.Windows.Controls.ContextMenu();
            var showItem = new System.Windows.Controls.MenuItem { Header = "显示悬浮球" };
            showItem.Click += (_, _) => ShowFromTray();
            menu.Items.Add(showItem);
            var settingsItem = new System.Windows.Controls.MenuItem { Header = "设置" };
            settingsItem.Click += (_, _) => ShowSettings();
            menu.Items.Add(settingsItem);
            menu.Items.Add(new System.Windows.Controls.Separator());
            var exitItem = new System.Windows.Controls.MenuItem { Header = "退出" };
            exitItem.Click += (_, _) => Application.Current.Shutdown();
            menu.Items.Add(exitItem);

            var trayPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tray.ico");
            var icon = File.Exists(trayPath) ? new Icon(trayPath) : SystemIcons.Application;

            _trayIcon = new TaskbarIcon
            {
                Icon = icon,
                ToolTipText = "小帮手",
                ContextMenu = menu
            };
            _trayIcon.ForceCreate();
        }
        catch
        {
        }
    }

    private void PositionBallTopRight()
    {
        var workArea = SystemParameters.WorkArea;
        _config.FloatingBallX = workArea.Right - 200;
        _config.FloatingBallY = workArea.Top + 100;
    }

    public void SwitchMode(UiMode mode)
    {
        if (_config.Mode == mode) return;

        SaveFloatingBallPosition();
        _config.Mode = mode;
        SaveState();

        if (mode == UiMode.FloatingBall)
        {
            var sidebar = _sidebar;
            _sidebar = null;
            sidebar?.Hide();
            ShowFloatingBall();
        }
        else
        {
            var ball = _floatingBall;
            _floatingBall = null;
            ball?.Hide();
            ShowSidebar();
        }

        ModeChanged?.Invoke(mode);
    }

    private void ShowFloatingBall()
    {
        try
        {
            _floatingBall = new FloatingBallWindow(this, _noteService, _quickActionsService);
            _floatingBall.Left = _config.FloatingBallX;
            _floatingBall.Top = _config.FloatingBallY;
            _floatingBall.Show();
            _floatingBall.Activate();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"创建悬浮球失败: {ex}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowSidebar()
    {
        try
        {
            _sidebar = new SidebarWindow(this, _clipboardService, _quickActionsService);
            _sidebar.Show();
            _sidebar.Activate();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"创建侧边栏失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void MinimizeToTray()
    {
        _floatingBall?.Hide();
        _sidebar?.Hide();
    }

    public void ShowFromTray()
    {
        if (_config.Mode != UiMode.FloatingBall)
        {
            SwitchMode(UiMode.FloatingBall);
            return;
        }

        if (_floatingBall == null)
            ShowFloatingBall();
        else
        {
            _floatingBall.Show();
            _floatingBall.Activate();
        }
    }

    private void SaveFloatingBallPosition()
    {
        if (_floatingBall != null)
        {
            _config.FloatingBallX = _floatingBall.Left;
            _config.FloatingBallY = _floatingBall.Top;
        }
    }

    public void SaveState()
    {
        try
        {
            _persistence.Save("config.json", _config);
        }
        catch
        {
        }
    }
}
