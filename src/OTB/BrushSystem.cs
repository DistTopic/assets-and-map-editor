using System.Xml.Linq;

namespace POriginsItemEditor.OTB;

// ═══════════════════════════════════════════════════════════════
//  Autobordering Brush System — ports the otacademy/RME algorithm
// ═══════════════════════════════════════════════════════════════

/// <summary>Border edge/corner type (1-12). Index into AutoBorder.Tiles[].</summary>
public enum BorderType : byte
{
    None = 0,
    NorthHorizontal = 1,
    EastHorizontal = 2,
    SouthHorizontal = 3,
    WestHorizontal = 4,
    NorthwestCorner = 5,
    NortheastCorner = 6,
    SouthwestCorner = 7,
    SoutheastCorner = 8,
    NorthwestDiagonal = 9,
    NortheastDiagonal = 10,
    SoutheastDiagonal = 11,
    SouthwestDiagonal = 12,
}

/// <summary>Bitmask for which of the 8 neighbours differ from this tile's brush.</summary>
[Flags]
public enum TileNeighbour : byte
{
    Northwest = 1,
    North = 2,
    Northeast = 4,
    West = 8,
    East = 16,
    Southwest = 32,
    South = 64,
    Southeast = 128,
}

/// <summary>A border definition (from borders.xml). Maps 12 edge positions → item IDs.</summary>
public sealed class AutoBorder
{
    public int Id { get; set; }
    public int Group { get; set; }

    /// <summary>Item IDs for each of the 13 border positions (0=none, 1-12=edge/corner).</summary>
    public ushort[] Tiles { get; } = new ushort[13];
}

/// <summary>A single border rule on a GroundBrush (from &lt;border&gt; elements).</summary>
public sealed class BorderBlock
{
    public bool IsOuter { get; set; } // outer = placed on the OTHER tile
    public string? TargetBrush { get; set; } // "none" or brush name; null = all
    public int AutoBorderId { get; set; } // references AutoBorder.Id
}

/// <summary>An item variation with weighted random chance.</summary>
public readonly record struct ItemChance(ushort ItemId, int Chance);

/// <summary>A ground brush definition (from grounds.xml).</summary>
public sealed class GroundBrush
{
    public string Name { get; set; } = string.Empty;
    public ushort LookId { get; set; }  // server_lookid
    public int ZOrder { get; set; }
    public List<ItemChance> Items { get; } = [];
    public int TotalChance { get; set; }
    public List<BorderBlock> Borders { get; } = [];
    public List<string> Friends { get; } = [];
    public int? OptionalBorderId { get; set; }
}

/// <summary>
/// The complete brush data: AutoBorders + GroundBrushes + item→brush mapping.
/// Loaded from borders.xml + grounds.xml.
/// </summary>
public sealed class BrushDatabase
{
    public Dictionary<int, AutoBorder> AutoBorders { get; } = [];
    public Dictionary<string, GroundBrush> GroundBrushes { get; } = [];

    /// <summary>Server item ID → ground brush name (built from all brush item entries).</summary>
    public Dictionary<ushort, string> ItemToBrush { get; } = [];

    /// <summary>Border item ID → AutoBorder ID (for cleaning borders).</summary>
    public HashSet<ushort> BorderItemIds { get; } = [];

    // ═══════════════════════════════════════════════════════════
    //  Loading
    // ═══════════════════════════════════════════════════════════

    public static BrushDatabase Load(string bordersXmlPath, string groundsXmlPath)
    {
        var db = new BrushDatabase();
        db.LoadBorders(bordersXmlPath);
        db.LoadGrounds(groundsXmlPath);
        return db;
    }

    private void LoadBorders(string path)
    {
        if (!File.Exists(path)) return;
        var doc = XDocument.Load(path);
        foreach (var el in doc.Root!.Elements("border"))
        {
            var ab = new AutoBorder
            {
                Id = (int)el.Attribute("id")!,
                Group = (int?)el.Attribute("group") ?? 0,
            };

            foreach (var item in el.Elements("borderitem"))
            {
                string edge = (string)item.Attribute("edge")!;
                ushort itemId = (ushort)(int)item.Attribute("item")!;
                int idx = EdgeToIndex(edge);
                if (idx > 0)
                {
                    ab.Tiles[idx] = itemId;
                    BorderItemIds.Add(itemId);
                }
            }

            AutoBorders[ab.Id] = ab;
        }
    }

    private void LoadGrounds(string path)
    {
        if (!File.Exists(path)) return;
        var doc = XDocument.Load(path);
        foreach (var el in doc.Root!.Elements("brush"))
        {
            string? type = (string?)el.Attribute("type");
            if (type != "ground") continue;

            string name = (string)el.Attribute("name")!;
            var brush = new GroundBrush
            {
                Name = name,
                LookId = (ushort)((int?)el.Attribute("server_lookid") ?? 0),
                ZOrder = (int?)el.Attribute("z-order") ?? 0,
            };

            // Item variations
            int totalChance = 0;
            foreach (var itemEl in el.Elements("item"))
            {
                ushort id = (ushort)(int)itemEl.Attribute("id")!;
                int chance = (int?)itemEl.Attribute("chance") ?? 0;
                totalChance += chance;
                brush.Items.Add(new ItemChance(id, totalChance));
                ItemToBrush[id] = name;
            }
            brush.TotalChance = totalChance;

            // Border rules
            foreach (var borderEl in el.Elements("border"))
            {
                brush.Borders.Add(new BorderBlock
                {
                    IsOuter = (string?)borderEl.Attribute("align") == "outer",
                    TargetBrush = (string?)borderEl.Attribute("to"),
                    AutoBorderId = (int)borderEl.Attribute("id")!,
                });
            }

            // Friends
            foreach (var friendEl in el.Elements("friend"))
                brush.Friends.Add((string)friendEl.Attribute("name")!);

            // Optional border
            var optEl = el.Element("optional");
            if (optEl != null)
                brush.OptionalBorderId = (int)optEl.Attribute("id")!;

            GroundBrushes[name] = brush;
        }
    }

    private static int EdgeToIndex(string edge) => edge switch
    {
        "n" => (int)BorderType.NorthHorizontal,
        "e" => (int)BorderType.EastHorizontal,
        "s" => (int)BorderType.SouthHorizontal,
        "w" => (int)BorderType.WestHorizontal,
        "cnw" => (int)BorderType.NorthwestCorner,
        "cne" => (int)BorderType.NortheastCorner,
        "csw" => (int)BorderType.SouthwestCorner,
        "cse" => (int)BorderType.SoutheastCorner,
        "dnw" => (int)BorderType.NorthwestDiagonal,
        "dne" => (int)BorderType.NortheastDiagonal,
        "dsw" => (int)BorderType.SouthwestDiagonal,
        "dse" => (int)BorderType.SoutheastDiagonal,
        _ => 0,
    };

    // ═══════════════════════════════════════════════════════════
    //  Autobordering Algorithm
    // ═══════════════════════════════════════════════════════════

    private static readonly Random _rng = new();

    /// <summary>Pick a random ground item for a brush (weighted).</summary>
    public ushort PickRandomItem(GroundBrush brush)
    {
        if (brush.Items.Count == 0) return 0;
        if (brush.TotalChance <= 0) return brush.Items[0].ItemId;

        int roll = _rng.Next(1, brush.TotalChance + 1);
        foreach (var ic in brush.Items)
        {
            if (roll <= ic.Chance) return ic.ItemId;
        }
        return brush.Items[^1].ItemId;
    }

    /// <summary>Get the GroundBrush for a tile's ground item, or null.</summary>
    public GroundBrush? GetBrushForItem(ushort serverId)
    {
        if (ItemToBrush.TryGetValue(serverId, out var name) &&
            GroundBrushes.TryGetValue(name, out var brush))
            return brush;
        return null;
    }

    /// <summary>Check if two brushes are friends (no borders between them).</summary>
    private bool AreFriends(GroundBrush a, GroundBrush b)
    {
        return a.Friends.Contains(b.Name) || b.Friends.Contains(a.Name);
    }

    /// <summary>
    /// Find the AutoBorder to use between two ground brushes.
    /// Returns (autoBorder, isOuter). isOuter means placed on the lower-z tile.
    /// </summary>
    private AutoBorder? GetBorderBetween(GroundBrush thisBrush, GroundBrush? otherBrush, out bool isOuter)
    {
        isOuter = false;

        // Check this brush's border rules
        foreach (var bb in thisBrush.Borders)
        {
            if (otherBrush == null)
            {
                // Bordering void/empty
                if (bb.TargetBrush == "none" && AutoBorders.TryGetValue(bb.AutoBorderId, out var ab))
                {
                    isOuter = bb.IsOuter;
                    return ab;
                }
            }
            else if (bb.TargetBrush == null) // applies to all
            {
                if (AutoBorders.TryGetValue(bb.AutoBorderId, out var ab))
                {
                    isOuter = bb.IsOuter;
                    return ab;
                }
            }
            else if (bb.TargetBrush == otherBrush.Name)
            {
                if (AutoBorders.TryGetValue(bb.AutoBorderId, out var ab))
                {
                    isOuter = bb.IsOuter;
                    return ab;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Offset table for 8 neighbours: NW, N, NE, W, E, SW, S, SE.
    /// </summary>
    private static readonly (int dx, int dy, TileNeighbour bit)[] NeighbourOffsets =
    [
        (-1, -1, TileNeighbour.Northwest),
        ( 0, -1, TileNeighbour.North),
        (+1, -1, TileNeighbour.Northeast),
        (-1,  0, TileNeighbour.West),
        (+1,  0, TileNeighbour.East),
        (-1, +1, TileNeighbour.Southwest),
        ( 0, +1, TileNeighbour.South),
        (+1, +1, TileNeighbour.Southeast),
    ];

    /// <summary>
    /// The 256-entry lookup table: neighbour bitmask → packed border types.
    /// Up to 4 BorderType values packed into one uint (bytes 0-3).
    /// Ported from otacademy brush_tables.cpp GroundBrush::init().
    /// </summary>
    private static readonly uint[] BorderTypes =
    [
        0x00000000, 0x00000005, 0x00000001, 0x00000001, 0x00000006, 0x00000605, 0x00000001, 0x00000001, // [0..7]
        0x00000004, 0x00000004, 0x00000009, 0x00000009, 0x00000604, 0x00000604, 0x00000009, 0x00000009, // [8..15]
        0x00000002, 0x00000205, 0x0000000A, 0x0000000A, 0x00000002, 0x00000205, 0x0000000A, 0x0000000A, // [16..23]
        0x00000204, 0x00000204, 0x00020401, 0x00020401, 0x00000402, 0x00000402, 0x00040201, 0x00040201, // [24..31]
        0x00000007, 0x00000507, 0x00000107, 0x00000107, 0x00000607, 0x00050607, 0x00000107, 0x00000107, // [32..39]
        0x00000004, 0x00000004, 0x00000009, 0x00000009, 0x00000604, 0x00000604, 0x00000009, 0x00000009, // [40..47]
        0x00000207, 0x00050207, 0x00000A07, 0x00000A07, 0x00000207, 0x00050207, 0x00000A07, 0x00000A07, // [48..55]
        0x00000204, 0x00000204, 0x00010204, 0x00010204, 0x00000204, 0x00000204, 0x00010204, 0x00010204, // [56..63]
        0x00000003, 0x00000503, 0x00000103, 0x00000103, 0x00000603, 0x00050603, 0x00000103, 0x00000103, // [64..71]
        0x0000000C, 0x0000000C, 0x00040103, 0x00040103, 0x0000060C, 0x0000060C, 0x00040103, 0x00040103, // [72..79]
        0x0000000B, 0x0000050B, 0x00020103, 0x00020103, 0x0000000B, 0x0000050B, 0x00020103, 0x00020103, // [80..87]
        0x00020403, 0x00020403, 0x01020403, 0x01020403, 0x00020403, 0x00020403, 0x01020403, 0x01020403, // [88..95]
        0x00000003, 0x00000503, 0x00000103, 0x00000103, 0x00000603, 0x00060503, 0x00000103, 0x00000103, // [96..103]
        0x0000000C, 0x0000000C, 0x00010403, 0x00010403, 0x0000060C, 0x0000060C, 0x00010403, 0x00010403, // [104..111]
        0x0000000B, 0x0000050B, 0x00010203, 0x00010203, 0x0000000B, 0x0000050B, 0x00010203, 0x00010203, // [112..119]
        0x00040203, 0x00040203, 0x04010203, 0x04010203, 0x00040203, 0x00040203, 0x04010203, 0x04010203, // [120..127]
        0x00000008, 0x00000805, 0x00000801, 0x00000801, 0x00000806, 0x00080506, 0x00000801, 0x00000801, // [128..135]
        0x00000804, 0x00000804, 0x00000809, 0x00000809, 0x00080604, 0x00080604, 0x00000809, 0x00000809, // [136..143]
        0x00000002, 0x00000502, 0x0000000A, 0x0000000A, 0x00000002, 0x00000502, 0x0000000A, 0x0000000A, // [144..151]
        0x00000402, 0x00000402, 0x00040201, 0x00010402, 0x00000402, 0x00000402, 0x00040201, 0x00040201, // [152..159]
        0x00000807, 0x00080507, 0x00080107, 0x00080107, 0x00080607, 0x08050607, 0x00080107, 0x00080107, // [160..167]
        0x00000804, 0x00000804, 0x00000809, 0x00000809, 0x00080604, 0x00080604, 0x00000809, 0x00000809, // [168..175]
        0x00000207, 0x00050207, 0x00000A07, 0x00000A07, 0x00000207, 0x00050207, 0x00000A07, 0x00000A07, // [176..183]
        0x00000204, 0x00000204, 0x00010204, 0x00010204, 0x00000204, 0x00000204, 0x00010204, 0x00010204, // [184..191]
        0x00000003, 0x00000503, 0x00000103, 0x00000103, 0x00000603, 0x00050603, 0x00000103, 0x00000103, // [192..199]
        0x0000000C, 0x0000000C, 0x00040103, 0x00040103, 0x0000060C, 0x0000060C, 0x00040103, 0x00040103, // [200..207]
        0x0000000B, 0x0000050B, 0x00020103, 0x00020103, 0x0000000B, 0x0000050B, 0x00020103, 0x00020103, // [208..215]
        0x00020403, 0x00020403, 0x01020403, 0x01020403, 0x00020403, 0x00020403, 0x01020403, 0x01020403, // [216..223]
        0x00000003, 0x00000503, 0x00000103, 0x00000103, 0x00000603, 0x00060503, 0x00000103, 0x00000103, // [224..231]
        0x0000000C, 0x0000000C, 0x00010403, 0x00010403, 0x0000060C, 0x0000060C, 0x00010403, 0x00010403, // [232..239]
        0x0000000B, 0x0000050B, 0x00010203, 0x00010203, 0x0000000B, 0x0000050B, 0x00010203, 0x00010203, // [240..247]
        0x00040203, 0x00040203, 0x04010203, 0x04010203, 0x00040203, 0x00040203, 0x04010203, 0x04010203, // [248..255]
    ];

    /// <summary>
    /// Core autobordering: recalculate all border items for one tile.
    /// Examines 8 neighbours, determines which borders are needed, places item IDs.
    /// Returns a list of (serverId) border items that should be on the tile.
    /// </summary>
    public List<ushort> ComputeBorders(MapData map, MapPosition pos)
    {
        var result = new List<ushort>();
        if (!map.Tiles.TryGetValue(pos, out var tile) || tile.Items.Count == 0)
            return result;

        // Find this tile's ground brush
        ushort groundId = tile.Items[0].Id;
        var thisBrush = GetBrushForItem(groundId);
        if (thisBrush == null) return result;

        // Collect border clusters from all 8 neighbours
        var clusters = new List<(int zOrder, AutoBorder border, byte tiledata)>();
        var visited = new HashSet<string>(); // track which neighbour brushes we already processed

        for (int ni = 0; ni < NeighbourOffsets.Length; ni++)
        {
            var (dx, dy, _) = NeighbourOffsets[ni];
            var nPos = new MapPosition(
                (ushort)(pos.X + dx),
                (ushort)(pos.Y + dy),
                pos.Z);

            GroundBrush? otherBrush = null;
            if (map.Tiles.TryGetValue(nPos, out var nTile) && nTile.Items.Count > 0)
                otherBrush = GetBrushForItem(nTile.Items[0].Id);

            // Same brush or friends → no border
            if (otherBrush != null && (otherBrush.Name == thisBrush.Name || AreFriends(thisBrush, otherBrush)))
                continue;

            string key = otherBrush?.Name ?? "__none__";
            if (visited.Contains(key)) continue;
            visited.Add(key);

            // Build tiledata bitmask: all neighbours that have this same "other" brush
            byte tiledata = 0;
            for (int j = 0; j < NeighbourOffsets.Length; j++)
            {
                var (dx2, dy2, bit) = NeighbourOffsets[j];
                var jPos = new MapPosition(
                    (ushort)(pos.X + dx2),
                    (ushort)(pos.Y + dy2),
                    pos.Z);

                GroundBrush? jBrush = null;
                if (map.Tiles.TryGetValue(jPos, out var jTile) && jTile.Items.Count > 0)
                    jBrush = GetBrushForItem(jTile.Items[0].Id);

                bool matches;
                if (otherBrush == null)
                    matches = jBrush == null || (jBrush.Name != thisBrush.Name && !AreFriends(thisBrush, jBrush));
                else
                    matches = jBrush?.Name == otherBrush.Name;

                if (matches) tiledata |= (byte)bit;
            }

            if (tiledata == 0) continue;

            // Determine which border to use
            // If other has higher z-order and has outer border → place other's outer border on THIS tile
            // Otherwise → place this brush's inner border
            AutoBorder? ab = null;
            if (otherBrush != null && otherBrush.ZOrder > thisBrush.ZOrder)
            {
                ab = FindOuterBorder(otherBrush, thisBrush);
                if (ab != null)
                {
                    clusters.Add((otherBrush.ZOrder, ab, tiledata));
                    continue;
                }
            }

            // Try this brush's inner border
            ab = FindInnerBorder(thisBrush, otherBrush);
            if (ab != null)
                clusters.Add((otherBrush?.ZOrder ?? 0, ab, tiledata));
        }

        // Sort by z-order (lower first = drawn first = behind higher-z borders)
        clusters.Sort((a, b) => a.zOrder.CompareTo(b.zOrder));

        // Resolve each cluster into item IDs using the lookup table
        foreach (var (_, border, tiledata) in clusters)
        {
            uint packed = BorderTypes[tiledata];
            for (int shift = 0; shift < 32; shift += 8)
            {
                int bt = (int)((packed >> shift) & 0xFF);
                if (bt == 0) break;

                if (bt >= 1 && bt <= 12 && border.Tiles[bt] != 0)
                {
                    result.Add(border.Tiles[bt]);
                }
                else if (bt >= 9 && bt <= 12)
                {
                    // Diagonal fallback: decompose into two edge pieces
                    var (e1, e2) = bt switch
                    {
                        9 => ((int)BorderType.WestHorizontal, (int)BorderType.NorthHorizontal),
                        10 => ((int)BorderType.EastHorizontal, (int)BorderType.NorthHorizontal),
                        11 => ((int)BorderType.EastHorizontal, (int)BorderType.SouthHorizontal),
                        12 => ((int)BorderType.WestHorizontal, (int)BorderType.SouthHorizontal),
                        _ => (0, 0),
                    };
                    if (e1 > 0 && border.Tiles[e1] != 0) result.Add(border.Tiles[e1]);
                    if (e2 > 0 && border.Tiles[e2] != 0) result.Add(border.Tiles[e2]);
                }
            }
        }

        return result;
    }

    private AutoBorder? FindOuterBorder(GroundBrush brush, GroundBrush? target)
    {
        foreach (var bb in brush.Borders)
        {
            if (!bb.IsOuter) continue;
            if (bb.TargetBrush == null || (target != null && bb.TargetBrush == target.Name))
            {
                if (AutoBorders.TryGetValue(bb.AutoBorderId, out var ab))
                    return ab;
            }
        }
        return null;
    }

    private AutoBorder? FindInnerBorder(GroundBrush brush, GroundBrush? target)
    {
        // First try specific target
        if (target != null)
        {
            foreach (var bb in brush.Borders)
            {
                if (bb.IsOuter) continue;
                if (bb.TargetBrush == target.Name && AutoBorders.TryGetValue(bb.AutoBorderId, out var ab))
                    return ab;
            }
        }

        // Then try "none" for void
        if (target == null)
        {
            foreach (var bb in brush.Borders)
            {
                if (bb.IsOuter) continue;
                if (bb.TargetBrush == "none" && AutoBorders.TryGetValue(bb.AutoBorderId, out var ab))
                    return ab;
            }
        }

        // Finally try catch-all
        foreach (var bb in brush.Borders)
        {
            if (bb.IsOuter) continue;
            if (bb.TargetBrush == null && AutoBorders.TryGetValue(bb.AutoBorderId, out var ab))
                return ab;
        }

        return null;
    }

    /// <summary>
    /// Apply autobordering to a tile and its 8 neighbours.
    /// Call this after placing or removing a ground item.
    /// Modifies the tile's Items list in-place: removes old borders, adds new ones.
    /// </summary>
    public void Borderize(MapData map, MapPosition centerPos)
    {
        // Borderize the center tile and all 8 neighbours
        var positions = new List<MapPosition> { centerPos };
        foreach (var (dx, dy, _) in NeighbourOffsets)
        {
            int nx = centerPos.X + dx;
            int ny = centerPos.Y + dy;
            if (nx < 0 || nx > 65535 || ny < 0 || ny > 65535) continue;
            positions.Add(new MapPosition((ushort)nx, (ushort)ny, centerPos.Z));
        }

        foreach (var pos in positions)
        {
            if (!map.Tiles.TryGetValue(pos, out var tile) || tile.Items.Count == 0)
                continue;

            // Remove existing border items (keep ground + non-border items)
            for (int i = tile.Items.Count - 1; i >= 1; i--)
            {
                if (BorderItemIds.Contains(tile.Items[i].Id))
                    tile.Items.RemoveAt(i);
            }

            // Compute new borders
            var borderIds = ComputeBorders(map, pos);

            // Insert borders right after ground (index 1+), before regular items
            int insertIdx = 1; // after ground
            foreach (var borderId in borderIds)
            {
                tile.Items.Insert(insertIdx, new MapItem { Id = borderId });
                insertIdx++;
            }
        }
    }
}
