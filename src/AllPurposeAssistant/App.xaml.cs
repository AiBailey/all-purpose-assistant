using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace AllPurposeAssistant;

public partial class App : Application
{
    private readonly ServiceProvider _serviceProvider;
    private static Mutex? _mutex;
    private static EventWaitHandle? _showFloatingBallEvent;
    private static RegisteredWaitHandle? _showFloatingBallWait;

    public App()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<Services.PersistenceService>();
        services.AddSingleton<Services.NoteService>();
        services.AddSingleton<Services.ClipboardService>();
        services.AddSingleton<Services.QuickActionsService>();
        services.AddSingleton<Services.ScreenshotService>();
        services.AddSingleton<Services.HotKeyManager>();
        services.AddSingleton<Services.StartupService>();
        services.AddSingleton<Services.WindowManager>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "AllPurposeAssistant_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            try
            {
                using var showEvent = EventWaitHandle.OpenExisting("AllPurposeAssistant_ShowFloatingBall");
                showEvent.Set();
            }
            catch
            {
            }
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var persistenceService = _serviceProvider.GetRequiredService<Services.PersistenceService>();
        var windowManager = _serviceProvider.GetRequiredService<Services.WindowManager>();
        var clipboardService = _serviceProvider.GetRequiredService<Services.ClipboardService>();

        _showFloatingBallEvent = new EventWaitHandle(false, EventResetMode.AutoReset,
            "AllPurposeAssistant_ShowFloatingBall");
        _showFloatingBallWait = ThreadPool.RegisterWaitForSingleObject(_showFloatingBallEvent,
            (_, _) => Dispatcher.BeginInvoke(windowManager.ShowFromTray), null, -1, false);
        clipboardService.Start();
        windowManager.Initialize();

        Exit += (_, _) =>
        {
            windowManager.SaveState();
            clipboardService.Dispose();
            _showFloatingBallWait?.Unregister(null);
            _showFloatingBallEvent?.Dispose();
            _mutex?.ReleaseMutex();
        };
    }
}
