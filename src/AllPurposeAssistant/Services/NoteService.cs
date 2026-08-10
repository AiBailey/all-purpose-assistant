using System.IO;
using AllPurposeAssistant.Models;

namespace AllPurposeAssistant.Services;

public class NoteService
{
    private const string DefaultNoteFileName = "default.json";
    private readonly PersistenceService _persistence;

    public NoteService(PersistenceService persistence)
    {
        _persistence = persistence;
    }

    public NoteItem GetOrCreateDefault()
    {
        var note = _persistence.Load<NoteItem>(Path.Combine("Notes", DefaultNoteFileName));
        if (note == null)
        {
            note = new NoteItem { Id = "default", Title = "便签" };
            Save(note);
        }
        return note;
    }

    public void Save(NoteItem note)
    {
        note.UpdatedAt = DateTime.Now;
        _persistence.Save(Path.Combine("Notes", $"{note.Id}.json"), note);
    }

    public void Delete(string id)
    {
        var path = _persistence.GetFullPath(Path.Combine("Notes", $"{id}.json"));
        if (File.Exists(path))
            File.Delete(path);
    }
}
