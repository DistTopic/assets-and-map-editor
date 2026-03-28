using System.Text.Json;

namespace AssetsAndMapEditor.App;

public class SavedSession
{
    public string? ClientFolderPath { get; set; }
    public string? OtbPath { get; set; }
    public string? MapFilePath { get; set; }
    public int ProtocolVersion { get; set; }
    public bool IsActive { get; set; }
    public double MapViewX { get; set; }
    public double MapViewY { get; set; }
    public byte MapCurrentFloor { get; set; } = 7;
    public double MapZoom { get; set; } = 1.0;
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
            "AssetsAndMapEditor",
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
