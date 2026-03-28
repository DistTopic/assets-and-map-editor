using System.Text.Json;

namespace AssetsAndMapEditor.OTB;

/// <summary>
/// Parsed entry from items.json — one per server ID.
/// </summary>
public sealed class CatalogItem
{
    public ushort ServerId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Article { get; init; }
    public Dictionary<string, string> Attributes { get; init; } = [];
}

/// <summary>
/// Loads and indexes the OpenCoreMMO items.json file,
/// providing fast lookup of item names and attributes by server ID.
/// </summary>
public sealed class ItemsCatalog
{
    private readonly Dictionary<ushort, CatalogItem> _items = [];

    public IReadOnlyDictionary<ushort, CatalogItem> Items => _items;
    public int Count => _items.Count;

    /// <summary>Try to get an item by its server ID.</summary>
    public bool TryGet(ushort serverId, out CatalogItem item) =>
        _items.TryGetValue(serverId, out item!);

    /// <summary>Get the display name for a server ID, or fallback.</summary>
    public string GetName(ushort serverId) =>
        _items.TryGetValue(serverId, out var item) ? item.Name : $"item #{serverId}";

    /// <summary>Load from an items.json file path.</summary>
    public static ItemsCatalog Load(string path)
    {
        var catalog = new ItemsCatalog();
        if (!File.Exists(path)) return catalog;

        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            string name = element.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
            string? article = element.TryGetProperty("article", out var artProp) ? artProp.GetString() : null;

            // Parse attributes
            Dictionary<string, string> attrs = [];
            if (element.TryGetProperty("attributes", out var attrArr) && attrArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var attr in attrArr.EnumerateArray())
                {
                    string key = attr.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";
                    string val = attr.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "";
                    if (key.Length > 0) attrs[key] = val;
                }
            }

            if (element.TryGetProperty("id", out var idProp))
            {
                if (ushort.TryParse(idProp.GetString(), out ushort id))
                {
                    catalog._items[id] = new CatalogItem
                    {
                        ServerId = id, Name = name, Article = article, Attributes = attrs
                    };
                }
            }
            else if (element.TryGetProperty("fromId", out var fromProp) &&
                     element.TryGetProperty("toId", out var toProp))
            {
                if (ushort.TryParse(fromProp.GetString(), out ushort from) &&
                    ushort.TryParse(toProp.GetString(), out ushort to))
                {
                    for (ushort i = from; i <= to; i++)
                    {
                        catalog._items[i] = new CatalogItem
                        {
                            ServerId = i, Name = name, Article = article, Attributes = attrs
                        };
                    }
                }
            }
        }

        return catalog;
    }
}
