using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AllPurposeAssistant.Services;

public class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AllPurposeAssistant";

    public bool IsLaunchAtLogin()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) != null;
    }

    public void SetLaunchAtLogin(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            var executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定程序路径。");
            key.SetValue(ValueName, $"\"{executablePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }

    public bool TryCreateDesktopShortcut(out string error)
    {
        error = "";
        try
        {
            var executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定程序路径。");
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var shortcutPath = Path.Combine(desktopPath, "小帮手.lnk");
            var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
            var shellType = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException("无法创建桌面快捷方式。");
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = executablePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(executablePath);
            shortcut.IconLocation = File.Exists(iconPath) ? iconPath + ",0" : executablePath + ",0";
            shortcut.Description = "小帮手";
            shortcut.Save();
            Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
