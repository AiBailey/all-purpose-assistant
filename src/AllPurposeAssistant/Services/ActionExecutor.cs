using System.IO;
using System.Diagnostics;
using AllPurposeAssistant.Models;

namespace AllPurposeAssistant.Services;

public class ActionExecutor
{
    public void Execute(QuickAction action)
    {
        if (string.IsNullOrWhiteSpace(action.Target) &&
            action.Type != ActionType.Shutdown &&
            action.Type != ActionType.Restart &&
            action.Type != ActionType.Lock)
            return;

        try
        {
            var psi = new ProcessStartInfo { UseShellExecute = true };

            switch (action.Type)
            {
                case ActionType.Shutdown:
                    psi.FileName = "shutdown";
                    psi.Arguments = "/s /t 0";
                    break;
                case ActionType.Restart:
                    psi.FileName = "shutdown";
                    psi.Arguments = "/r /t 0";
                    break;
                case ActionType.Lock:
                    psi.FileName = "rundll32.exe";
                    psi.Arguments = "user32.dll,LockWorkStation";
                    break;
                case ActionType.OpenApp:
                    if (string.IsNullOrEmpty(action.Target))
                        return;
                    psi.FileName = action.Target;
                    break;
                case ActionType.OpenFolder:
                    psi.FileName = "explorer.exe";
                    psi.Arguments = action.Target;
                    break;
                case ActionType.OpenUrl:
                    psi.FileName = "msedge.exe";
                    psi.Arguments = $"\"{action.Target}\"";
                    break;
                case ActionType.ShellCommand:
                    psi.FileName = "cmd.exe";
                    psi.Arguments = "/c " + action.Target;
                    break;
                default:
                    psi.FileName = action.Target;
                    break;
            }

            Process.Start(psi);
        }
        catch
        {
        }
    }
}
