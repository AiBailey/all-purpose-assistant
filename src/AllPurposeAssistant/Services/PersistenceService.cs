using System.IO;

using Newtonsoft.Json;

namespace AllPurposeAssistant.Services;

public class PersistenceService
{
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AllPurposeAssistant");

    public PersistenceService()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(Path.Combine(DataDir, "Images"));
        Directory.CreateDirectory(Path.Combine(DataDir, "Notes"));
    }

    public T? Load<T>(string fileName) where T : class
    {
        var path = GetPath(fileName);
        if (!File.Exists(path))
            return null;

        var json = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<T>(json);
    }

    public void Save<T>(string fileName, T data)
    {
        var path = GetPath(fileName);
        var json = JsonConvert.SerializeObject(data, Formatting.Indented);
        File.WriteAllText(path, json);
    }

    public string GetFullPath(string relativePath)
    {
        return Path.Combine(DataDir, relativePath);
    }

    private string GetPath(string fileName)
    {
        return Path.Combine(DataDir, fileName);
    }
}
