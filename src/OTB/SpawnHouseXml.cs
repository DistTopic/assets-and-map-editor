using System.Globalization;
using System.Xml;

namespace AssetsAndMapEditor.OTB;

/// <summary>
/// Reads and writes the external spawn and house XML files referenced by OTBM maps.
/// </summary>
public static class SpawnHouseXml
{
    // ═══════════════════════════════════════════════════
    //  SPAWNS
    // ═══════════════════════════════════════════════════

    /// <summary>Load spawns from a spawn XML file.</summary>
    public static List<MapSpawn> LoadSpawns(string filePath)
    {
        var spawns = new List<MapSpawn>();
        if (!File.Exists(filePath)) return spawns;

        var doc = new XmlDocument();
        doc.Load(filePath);

        var root = doc.DocumentElement;
        if (root == null || root.Name != "spawns") return spawns;

        foreach (XmlNode spawnNode in root.ChildNodes)
        {
            if (spawnNode.Name != "spawn") continue;
            var spawn = new MapSpawn
            {
                CenterX = GetUShort(spawnNode, "centerx"),
                CenterY = GetUShort(spawnNode, "centery"),
                CenterZ = GetByte(spawnNode, "centerz"),
                Radius = GetInt(spawnNode, "radius", 5),
            };

            foreach (XmlNode creatureNode in spawnNode.ChildNodes)
            {
                string tag = creatureNode.Name.ToLowerInvariant();
                if (tag != "monster" && tag != "npc") continue;

                spawn.Creatures.Add(new SpawnCreature
                {
                    Name = GetString(creatureNode, "name"),
                    IsNpc = tag == "npc",
                    RelX = GetInt(creatureNode, "x"),
                    RelY = GetInt(creatureNode, "y"),
                    SpawnTime = GetInt(creatureNode, "spawntime", 60),
                    Direction = GetInt(creatureNode, "direction"),
                });
            }

            spawns.Add(spawn);
        }

        return spawns;
    }

    /// <summary>Save spawns to a spawn XML file.</summary>
    public static void SaveSpawns(string filePath, IReadOnlyList<MapSpawn> spawns)
    {
        var settings = new XmlWriterSettings { Indent = true, IndentChars = "\t" };
        using var writer = XmlWriter.Create(filePath, settings);

        writer.WriteStartDocument();
        writer.WriteStartElement("spawns");

        foreach (var spawn in spawns)
        {
            writer.WriteStartElement("spawn");
            writer.WriteAttributeString("centerx", spawn.CenterX.ToString());
            writer.WriteAttributeString("centery", spawn.CenterY.ToString());
            writer.WriteAttributeString("centerz", spawn.CenterZ.ToString());
            writer.WriteAttributeString("radius", spawn.Radius.ToString());

            foreach (var creature in spawn.Creatures)
            {
                writer.WriteStartElement(creature.IsNpc ? "npc" : "monster");
                writer.WriteAttributeString("name", creature.Name);
                writer.WriteAttributeString("x", creature.RelX.ToString());
                writer.WriteAttributeString("y", creature.RelY.ToString());
                writer.WriteAttributeString("z", spawn.CenterZ.ToString());
                writer.WriteAttributeString("spawntime", creature.SpawnTime.ToString());
                if (creature.Direction != 0)
                    writer.WriteAttributeString("direction", creature.Direction.ToString());
                writer.WriteEndElement();
            }

            writer.WriteEndElement(); // spawn
        }

        writer.WriteEndElement(); // spawns
        writer.WriteEndDocument();
    }

    // ═══════════════════════════════════════════════════
    //  HOUSES
    // ═══════════════════════════════════════════════════

    /// <summary>Load house definitions from a house XML file.</summary>
    public static List<MapHouse> LoadHouses(string filePath)
    {
        var houses = new List<MapHouse>();
        if (!File.Exists(filePath)) return houses;

        var doc = new XmlDocument();
        doc.Load(filePath);

        var root = doc.DocumentElement;
        if (root == null || root.Name != "houses") return houses;

        foreach (XmlNode houseNode in root.ChildNodes)
        {
            if (houseNode.Name != "house") continue;
            houses.Add(new MapHouse
            {
                Id = GetUInt(houseNode, "houseid"),
                Name = GetString(houseNode, "name"),
                EntryX = GetUShort(houseNode, "entryx"),
                EntryY = GetUShort(houseNode, "entryy"),
                EntryZ = GetByte(houseNode, "entryz"),
                Rent = GetInt(houseNode, "rent"),
                TownId = GetUInt(houseNode, "townid"),
                Guildhall = GetBool(houseNode, "guildhall"),
            });
        }

        return houses;
    }

    /// <summary>Save house definitions to a house XML file.</summary>
    public static void SaveHouses(string filePath, IReadOnlyList<MapHouse> houses)
    {
        var settings = new XmlWriterSettings { Indent = true, IndentChars = "\t" };
        using var writer = XmlWriter.Create(filePath, settings);

        writer.WriteStartDocument();
        writer.WriteStartElement("houses");

        foreach (var house in houses)
        {
            writer.WriteStartElement("house");
            writer.WriteAttributeString("houseid", house.Id.ToString());
            writer.WriteAttributeString("name", house.Name);
            writer.WriteAttributeString("entryx", house.EntryX.ToString());
            writer.WriteAttributeString("entryy", house.EntryY.ToString());
            writer.WriteAttributeString("entryz", house.EntryZ.ToString());
            writer.WriteAttributeString("rent", house.Rent.ToString());
            writer.WriteAttributeString("townid", house.TownId.ToString());
            if (house.Guildhall)
                writer.WriteAttributeString("guildhall", "true");
            writer.WriteEndElement(); // house
        }

        writer.WriteEndElement(); // houses
        writer.WriteEndDocument();
    }

    // ═══════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════

    private static string GetString(XmlNode node, string attr, string def = "")
        => node.Attributes?[attr]?.Value ?? def;

    private static int GetInt(XmlNode node, string attr, int def = 0)
        => int.TryParse(node.Attributes?[attr]?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;

    private static uint GetUInt(XmlNode node, string attr, uint def = 0)
        => uint.TryParse(node.Attributes?[attr]?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;

    private static ushort GetUShort(XmlNode node, string attr, ushort def = 0)
        => ushort.TryParse(node.Attributes?[attr]?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;

    private static byte GetByte(XmlNode node, string attr, byte def = 0)
        => byte.TryParse(node.Attributes?[attr]?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;

    private static bool GetBool(XmlNode node, string attr, bool def = false)
    {
        var val = node.Attributes?[attr]?.Value;
        if (val == null) return def;
        return val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
    }
}
