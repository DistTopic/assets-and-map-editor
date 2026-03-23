using System.Text.Json;
using System.Text.Json.Serialization;

namespace POriginsItemEditor.App;

/// <summary>
/// Persistent user-created palette configuration.
/// Stored in the app data folder as palette.json.
/// Contains only collection structure and server IDs — all other
/// item data is resolved at runtime from OTB/items.json.
/// </summary>
public sealed class PaletteConfig
{
    public List<PaletteCollection> Collections { get; set; } = [];
    public List<PaletteCustomBrush> CustomBrushes { get; set; } = [];

    private static string ConfigPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "POriginsItemEditor",
            "palette.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static PaletteConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<PaletteConfig>(json, JsonOpts) ?? new();
            }
        }
        catch { }
        return new PaletteConfig();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch { }
    }
}

/// <summary>A top-level collection (e.g., "Terrains", "Walls", "Decorations").</summary>
public sealed class PaletteCollection
{
    public string Name { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public List<PaletteSubCollection> SubCollections { get; set; } = [];
}

/// <summary>A sub-collection within a collection (e.g., "Grass Tiles", "Stone Walls").</summary>
public sealed class PaletteSubCollection
{
    public string Name { get; set; } = string.Empty;
    public List<ushort> Items { get; set; } = [];
    public List<PaletteSubSubCollection> SubSubCollections { get; set; } = [];
}

/// <summary>A sub-sub-collection within a sub-collection (third tier).</summary>
public sealed class PaletteSubSubCollection
{
    public string Name { get; set; } = string.Empty;
    public List<ushort> Items { get; set; } = [];
}

/// <summary>A custom brush: paints randomly from its item set for variety.</summary>
public sealed class PaletteCustomBrush
{
    public string Name { get; set; } = string.Empty;
    public List<ushort> Items { get; set; } = [];
}
