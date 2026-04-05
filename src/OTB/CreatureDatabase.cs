using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace AssetsAndMapEditor.OTB;

/// <summary>A creature definition loaded from creatures.xml.</summary>
public sealed class CreatureEntry
{
    public string Name { get; set; } = string.Empty;
    public bool IsNpc { get; set; }
    public int LookType { get; set; }
    public int LookItem { get; set; }
    public int LookHead { get; set; }
    public int LookBody { get; set; }
    public int LookLegs { get; set; }
    public int LookFeet { get; set; }
    public int LookAddon { get; set; }
    public int LookMount { get; set; }

    public override string ToString() => Name;
}

/// <summary>Loads and queries the creature database from creatures.xml.</summary>
public static class CreatureDatabase
{
    /// <summary>Loads creatures from an XML file.</summary>
    public static List<CreatureEntry> LoadFromXml(string path)
    {
        var list = new List<CreatureEntry>();
        if (!File.Exists(path)) return list;

        var doc = XDocument.Load(path);
        var root = doc.Root;
        if (root == null) return list;

        foreach (var el in root.Elements("creature"))
        {
            var name = (string?)el.Attribute("name") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name)) continue;

            var typeStr = ((string?)el.Attribute("type") ?? "monster").ToLowerInvariant();

            list.Add(new CreatureEntry
            {
                Name = name,
                IsNpc = typeStr == "npc",
                LookType = SafeInt(el, "looktype"),
                LookItem = SafeInt(el, "lookitem"),
                LookHead = SafeInt(el, "lookhead"),
                LookBody = SafeInt(el, "lookbody"),
                LookLegs = SafeInt(el, "looklegs"),
                LookFeet = SafeInt(el, "lookfeet"),
                LookAddon = SafeInt(el, "lookaddon"),
                LookMount = SafeInt(el, "lookmount"),
            });
        }

        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    /// <summary>Searches for a creatures.xml alongside a map file or in common data paths.</summary>
    public static string? FindCreaturesXml(string? mapFilePath, string? clientFolderPath)
    {
        // Try next to map file
        if (!string.IsNullOrEmpty(mapFilePath))
        {
            var mapDir = Path.GetDirectoryName(mapFilePath);
            if (mapDir != null)
            {
                var p = Path.Combine(mapDir, "creatures.xml");
                if (File.Exists(p)) return p;
            }
        }

        // Try next to client folder (data/)
        if (!string.IsNullOrEmpty(clientFolderPath))
        {
            var p = Path.Combine(clientFolderPath, "creatures.xml");
            if (File.Exists(p)) return p;
            p = Path.Combine(clientFolderPath, "data", "creatures.xml");
            if (File.Exists(p)) return p;
        }

        return null;
    }

    private static int SafeInt(XElement el, string attr)
    {
        var val = (string?)el.Attribute(attr);
        return int.TryParse(val, out var n) ? n : 0;
    }
}
