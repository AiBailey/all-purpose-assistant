namespace AllPurposeAssistant.Models;

public enum ClipboardEntryType
{
    Text,
    Image
}

public class ClipboardEntry
{
    public ClipboardEntryType Type { get; set; }
    public string Text { get; set; } = "";
    public string? ImagePath { get; set; }
    public string? ImageFingerprint { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;

    public string Preview =>
        Type == ClipboardEntryType.Text
            ? (Text.Length > 30 ? Text[..30] + "…" : Text)
            : "[图片]";
}
