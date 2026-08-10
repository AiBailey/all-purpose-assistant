namespace AllPurposeAssistant.Models;

public class NoteItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public List<string> ImagePaths { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string Color { get; set; } = "#FFF9E6";
}
