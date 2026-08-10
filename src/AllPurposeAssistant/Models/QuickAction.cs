namespace AllPurposeAssistant.Models;

public enum ActionType
{
    Shutdown,
    Restart,
    Lock,
    OpenApp,
    OpenFolder,
    OpenUrl,
    ShellCommand
}

public class QuickAction
{
    public string Name { get; set; } = "";
    public ActionType Type { get; set; }
    public string Target { get; set; } = "";
    public bool IsFixed { get; set; }

    public string Icon => this switch
    {
        { IsFixed: true, Type: ActionType.Shutdown } => "⏻",
        { IsFixed: true, Type: ActionType.Restart } => "⟳",
        { IsFixed: true, Type: ActionType.Lock } => "⚿",
        { Type: ActionType.OpenFolder } => "⌂",
        { Type: ActionType.OpenUrl } => "◎",
        _ => "⚡"
    };
}
