using System.Text.Json;

namespace POriginsItemEditor.App;

public class SavedSession
{
    public string? ClientFolderPath { get; set; }
    public string? OtbPath { get; set; }
    public int ProtocolVersion { get; set; }
    public bool IsActive { get; set; }
}

public class AppSettings
{
    public string? LastOtbPath { get; set; }
    public string? LastClientFolderPath { get; set; }
    public List<SavedSession> Sessions { get; set; } = [];
    public Dictionary<string, bool> ViewSettings { get; set; } = [];

    private static string SettingsFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "POriginsItemEditor",
            "settings.json");

    public static AppSettings Load()
    {
        try
        {
            var path = SettingsFilePath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var path = SettingsFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch { }
    }
}
