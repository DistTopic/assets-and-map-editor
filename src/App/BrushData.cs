using System.Xml.Linq;

namespace POriginsItemEditor.App;

// ═══════════════════════════════════════════════════════════════════
// Brush data model — loaded from XML, used by palette + map editor
// ═══════════════════════════════════════════════════════════════════

public sealed class BrushCatalog
{
    public Dictionary<int, BorderDef> Borders { get; set; } = [];
    public List<GroundBrushDef> Grounds { get; set; } = [];
    public List<WallBrushDef> Walls { get; set; } = [];
    public List<DoodadBrushDef> Doodads { get; set; } = [];
    public List<CreatureDef> Creatures { get; set; } = [];
    public List<TilesetDef> Tilesets { get; set; } = [];

    /// <summary>Lookup: brush name → ground brush def.</summary>
    public Dictionary<string, GroundBrushDef> GroundsByName { get; set; } = [];
    /// <summary>Lookup: brush name → wall brush def.</summary>
    public Dictionary<string, WallBrushDef> WallsByName { get; set; } = [];
    /// <summary>Lookup: brush name → doodad brush def.</summary>
    public Dictionary<string, DoodadBrushDef> DoodadsByName { get; set; } = [];
    /// <summary>Reverse lookup: wall item server ID → owning WallBrushDef.</summary>
    public Dictionary<ushort, WallBrushDef> WallItemToBrush { get; set; } = [];

    /// <summary>Build lookup dictionaries after loading.</summary>
    public void BuildIndexes()
    {
        GroundsByName = Grounds.Where(g => g.Name.Length > 0).GroupBy(g => g.Name).ToDictionary(g => g.Key, g => g.Last());
        WallsByName = Walls.Where(w => w.Name.Length > 0).GroupBy(w => w.Name).ToDictionary(g => g.Key, g => g.Last());
        DoodadsByName = Doodads.Where(d => d.Name.Length > 0).GroupBy(d => d.Name).ToDictionary(g => g.Key, g => g.Last());

        // Build reverse lookup: every item in every wall segment → its parent WallBrushDef
        var wallItemMap = new Dictionary<ushort, WallBrushDef>();
        foreach (var wall in Walls)
        {
            foreach (var seg in wall.Segments.Values)
                foreach (var ci in seg.Items)
                    if (ci.Id > 0)
                        wallItemMap[ci.Id] = wall;
            foreach (var seg in wall.Segments.Values)
                foreach (var door in seg.Doors)
                    if (door.Id > 0)
                        wallItemMap[door.Id] = wall;
        }
        WallItemToBrush = wallItemMap;
    }

    /// <summary>Get the server_lookid for a brush name (any type).</summary>
    public ushort GetBrushLookId(string name)
    {
        if (GroundsByName.TryGetValue(name, out var g)) return g.LookId;
        if (WallsByName.TryGetValue(name, out var w)) return w.LookId;
        if (DoodadsByName.TryGetValue(name, out var d)) return d.LookId;
        return 0;
    }
}

// ── Border ──

public sealed class BorderDef
{
    public int Id { get; set; }
    public int Group { get; set; }
    public bool Optional { get; set; }
    public Dictionary<string, ushort> Edges { get; set; } = [];
}

// ── Ground brush ──

public sealed class GroundBrushDef
{
    public string Name { get; set; } = "";
    public ushort LookId { get; set; }
    public int ZOrder { get; set; }
    public bool Randomize { get; set; } = true;
    public List<ChanceItem> Items { get; set; } = [];
    public List<GroundBorderRef> Borders { get; set; } = [];
    public List<string> Friends { get; set; } = [];
}

public sealed class ChanceItem
{
    public ushort Id { get; set; }
    public int Chance { get; set; }
}

public sealed class GroundBorderRef
{
    public string Align { get; set; } = ""; // "outer", "inner"
    public int BorderId { get; set; }
    public string? To { get; set; } // brush name or "none"
    public bool Super { get; set; }
    /// <summary>True when this is an inline border definition (has borderitem children, no id).</summary>
    public bool Inline { get; set; }
    public ushort GroundEquivalent { get; set; }
    public Dictionary<string, ushort> InlineEdges { get; set; } = [];
}

// ── Wall brush ──

public sealed class WallBrushDef
{
    public string Name { get; set; } = "";
    public ushort LookId { get; set; }
    public Dictionary<string, WallSegment> Segments { get; set; } = []; // "horizontal","vertical","corner","pole"
}

public sealed class WallSegment
{
    public List<ChanceItem> Items { get; set; } = [];
    public List<WallDoor> Doors { get; set; } = [];
}

public sealed class WallDoor
{
    public ushort Id { get; set; }
    public string Type { get; set; } = ""; // "normal","locked","quest","magic","window","hatch_window"
    public bool Open { get; set; }
    public bool Locked { get; set; }
}

// ── Doodad brush ──

public sealed class DoodadBrushDef
{
    public string Name { get; set; } = "";
    public ushort LookId { get; set; }
    public bool Draggable { get; set; }
    public bool OnBlocking { get; set; }
    public bool OnDuplicate { get; set; }
    public bool RedoBorders { get; set; }
    public int ThicknessNum { get; set; } = 1;
    public int ThicknessDen { get; set; } = 1;
    public List<ChanceItem> Items { get; set; } = [];
    public List<DoodadComposite> Composites { get; set; } = [];
    public List<DoodadAlternate> Alternates { get; set; } = [];
}

public sealed class DoodadComposite
{
    public int Chance { get; set; }
    public List<CompositeTile> Tiles { get; set; } = [];
}

public sealed class CompositeTile
{
    public int X { get; set; }
    public int Y { get; set; }
    public List<ushort> Items { get; set; } = [];
}

public sealed class DoodadAlternate
{
    public List<ChanceItem> Items { get; set; } = [];
    public List<DoodadComposite> Composites { get; set; } = [];
}

// ── Creature ──

public sealed class CreatureDef
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "monster";
    public int LookType { get; set; }
    public int LookItem { get; set; }
    public int LookHead { get; set; }
    public int LookBody { get; set; }
    public int LookLegs { get; set; }
    public int LookFeet { get; set; }
}

// ── Tileset ──

public sealed class TilesetDef
{
    public string Name { get; set; } = "";
    public List<TilesetCategory> Categories { get; set; } = [];
}

public sealed class TilesetCategory
{
    public string Type { get; set; } = "raw"; // "raw", "terrain", "doodad", "items_and_raw", "terrain_and_raw"
    public List<TilesetEntry> Entries { get; set; } = [];
}

public sealed class TilesetEntry
{
    public string Type { get; set; } = "raw"; // "raw", "brush"
    public ushort ItemId { get; set; }    // for raw
    public ushort ItemIdEnd { get; set; } // for fromid/toid ranges (0 = single)
    public string? BrushName { get; set; } // for brush references
    public string? DisplayName { get; set; } // optional name override
}

// ═══════════════════════════════════════════════════════════════════
// XML Loader — reads the standard Tibia editor brush XML format
// ═══════════════════════════════════════════════════════════════════

public static class BrushXmlLoader
{
    public static BrushCatalog LoadFromDirectory(string dir)
    {
        var db = new BrushCatalog();

        var bordersPath = Path.Combine(dir, "borders.xml");
        var groundsPath = Path.Combine(dir, "grounds.xml");
        var wallsPath = Path.Combine(dir, "walls.xml");
        var doodadsPath = Path.Combine(dir, "doodads.xml");
        var creaturesPath = Path.Combine(dir, "creatures.xml");
        var tilesetsPath = Path.Combine(dir, "tilesets.xml");

        if (File.Exists(bordersPath)) LoadBorders(bordersPath, db);
        if (File.Exists(groundsPath)) LoadGrounds(groundsPath, db);
        if (File.Exists(wallsPath)) LoadWalls(wallsPath, db);
        if (File.Exists(doodadsPath)) LoadDoodads(doodadsPath, db);
        if (File.Exists(creaturesPath)) LoadCreatures(creaturesPath, db);
        if (File.Exists(tilesetsPath)) LoadTilesets(tilesetsPath, db);

        db.BuildIndexes();
        return db;
    }

    private static void LoadBorders(string path, BrushCatalog db)
    {
        var doc = XDocument.Load(path);
        foreach (var el in doc.Descendants("border"))
        {
            var def = new BorderDef
            {
                Id = (int?)el.Attribute("id") ?? 0,
                Group = (int?)el.Attribute("group") ?? 0,
                Optional = (string?)el.Attribute("type") == "optional",
            };
            foreach (var item in el.Elements("borderitem"))
            {
                var edge = (string?)item.Attribute("edge") ?? "";
                var itemId = (ushort?)(int?)item.Attribute("item") ?? 0;
                if (itemId > 0) def.Edges[edge] = itemId;
            }
            db.Borders[def.Id] = def;
        }
    }

    private static void LoadGrounds(string path, BrushCatalog db)
    {
        var doc = XDocument.Load(path);
        foreach (var el in doc.Descendants("brush"))
        {
            var type = (string?)el.Attribute("type");
            if (type != "ground") continue;

            var def = new GroundBrushDef
            {
                Name = (string?)el.Attribute("name") ?? "",
                LookId = (ushort?)(int?)el.Attribute("server_lookid") ?? 0,
                ZOrder = (int?)el.Attribute("z-order") ?? 0,
                Randomize = (string?)el.Attribute("randomize") != "false",
            };

            foreach (var item in el.Elements("item"))
            {
                def.Items.Add(new ChanceItem
                {
                    Id = (ushort?)(int?)item.Attribute("id") ?? 0,
                    Chance = (int?)item.Attribute("chance") ?? 0,
                });
            }

            foreach (var border in el.Elements("border"))
            {
                var bref = new GroundBorderRef
                {
                    Align = (string?)border.Attribute("align") ?? "",
                    BorderId = (int?)border.Attribute("id") ?? 0,
                    To = (string?)border.Attribute("to"),
                    Super = (string?)border.Attribute("super") == "true",
                };
                // Handle inline border definitions (have <borderitem> children but no id)
                if (border.Attribute("id") == null && border.HasElements)
                {
                    bref.Inline = true;
                    bref.GroundEquivalent = (ushort?)(int?)border.Attribute("ground_equivalent") ?? 0;
                    foreach (var bi in border.Elements("borderitem"))
                    {
                        var edge = (string?)bi.Attribute("edge") ?? "";
                        var itemId = (ushort?)(int?)bi.Attribute("item") ?? 0;
                        if (itemId > 0) bref.InlineEdges[edge] = itemId;
                    }
                }
                def.Borders.Add(bref);
            }

            foreach (var friend in el.Elements("friend"))
            {
                var name = (string?)friend.Attribute("name");
                if (name != null) def.Friends.Add(name);
            }

            // Skip empty stubs (brushes defined without items, e.g. "ice" with no server_lookid)
            if (def.LookId > 0 || def.Items.Count > 0)
                db.Grounds.Add(def);
        }
    }

    private static void LoadWalls(string path, BrushCatalog db)
    {
        var doc = XDocument.Load(path);
        foreach (var el in doc.Descendants("brush"))
        {
            var type = (string?)el.Attribute("type");
            if (type != "wall") continue;

            var def = new WallBrushDef
            {
                Name = (string?)el.Attribute("name") ?? "",
                LookId = (ushort?)(int?)el.Attribute("server_lookid") ?? 0,
            };

            foreach (var wall in el.Elements("wall"))
            {
                var segType = (string?)wall.Attribute("type") ?? "";
                var seg = new WallSegment();

                foreach (var item in wall.Elements("item"))
                {
                    seg.Items.Add(new ChanceItem
                    {
                        Id = (ushort?)(int?)item.Attribute("id") ?? 0,
                        Chance = (int?)item.Attribute("chance") ?? 0,
                    });
                }

                foreach (var door in wall.Elements("door"))
                {
                    seg.Doors.Add(new WallDoor
                    {
                        Id = (ushort?)(int?)door.Attribute("id") ?? 0,
                        Type = (string?)door.Attribute("type") ?? "",
                        Open = (string?)door.Attribute("open") == "true",
                        Locked = (string?)door.Attribute("locked") == "true",
                    });
                }

                def.Segments[segType] = seg;
            }

            db.Walls.Add(def);
        }
    }

    private static void LoadDoodads(string path, BrushCatalog db)
    {
        var doc = XDocument.Load(path);
        foreach (var el in doc.Descendants("brush"))
        {
            var type = (string?)el.Attribute("type");
            if (type != "doodad") continue;

            var def = new DoodadBrushDef
            {
                Name = (string?)el.Attribute("name") ?? "",
                LookId = (ushort?)(int?)el.Attribute("server_lookid") ?? 0,
                Draggable = (string?)el.Attribute("draggable") == "true",
                OnBlocking = (string?)el.Attribute("on_blocking") == "true",
                OnDuplicate = (string?)el.Attribute("on_duplicate") == "true",
                RedoBorders = (string?)el.Attribute("redo_borders") == "true",
            };

            // Parse thickness "N/D"
            var thickness = (string?)el.Attribute("thickness");
            if (thickness != null)
            {
                var parts = thickness.Split('/');
                if (parts.Length == 2 && int.TryParse(parts[0], out var num) && int.TryParse(parts[1], out var den))
                {
                    def.ThicknessNum = num;
                    def.ThicknessDen = den;
                }
            }

            foreach (var item in el.Elements("item"))
            {
                def.Items.Add(new ChanceItem
                {
                    Id = (ushort?)(int?)item.Attribute("id") ?? 0,
                    Chance = (int?)item.Attribute("chance") ?? 0,
                });
            }

            foreach (var comp in el.Elements("composite"))
            {
                def.Composites.Add(ParseComposite(comp));
            }

            foreach (var alt in el.Elements("alternate"))
            {
                var altDef = new DoodadAlternate();
                foreach (var item in alt.Elements("item"))
                {
                    altDef.Items.Add(new ChanceItem
                    {
                        Id = (ushort?)(int?)item.Attribute("id") ?? 0,
                        Chance = (int?)item.Attribute("chance") ?? 0,
                    });
                }
                foreach (var comp in alt.Elements("composite"))
                {
                    altDef.Composites.Add(ParseComposite(comp));
                }
                def.Alternates.Add(altDef);
            }

            db.Doodads.Add(def);
        }
    }

    private static DoodadComposite ParseComposite(XElement comp)
    {
        var result = new DoodadComposite
        {
            Chance = (int?)comp.Attribute("chance") ?? 0,
        };
        foreach (var tile in comp.Elements("tile"))
        {
            var ct = new CompositeTile
            {
                X = (int?)tile.Attribute("x") ?? 0,
                Y = (int?)tile.Attribute("y") ?? 0,
            };
            foreach (var item in tile.Elements("item"))
            {
                var id = (ushort?)(int?)item.Attribute("id") ?? 0;
                if (id > 0) ct.Items.Add(id);
            }
            result.Tiles.Add(ct);
        }
        return result;
    }

    private static void LoadCreatures(string path, BrushCatalog db)
    {
        var doc = XDocument.Load(path);
        foreach (var el in doc.Descendants("creature"))
        {
            db.Creatures.Add(new CreatureDef
            {
                Name = (string?)el.Attribute("name") ?? "",
                Type = (string?)el.Attribute("type") ?? "monster",
                LookType = SafeInt(el, "looktype"),
                LookItem = SafeInt(el, "lookitem"),
                LookHead = SafeInt(el, "lookhead"),
                LookBody = SafeInt(el, "lookbody"),
                LookLegs = SafeInt(el, "looklegs"),
                LookFeet = SafeInt(el, "lookfeet"),
            });
        }
    }

    /// <summary>Safely parse an integer attribute, returning 0 for missing/empty/invalid values.</summary>
    private static int SafeInt(XElement el, string attr)
    {
        var s = (string?)el.Attribute(attr);
        return int.TryParse(s, out var v) ? v : 0;
    }

    private static void LoadTilesets(string path, BrushCatalog db)
    {
        var doc = XDocument.Load(path);
        foreach (var ts in doc.Descendants("tileset"))
        {
            var def = new TilesetDef
            {
                Name = (string?)ts.Attribute("name") ?? "",
            };

            // Parse each category section (all types from OTAcademy map editor)
            string[] sectionNames = [
                "raw", "terrain", "doodad", "items", "creatures",
                "items_and_raw", "terrain_and_raw", "doodad_and_raw",
                "collections", "collections_and_terrain",
            ];
            foreach (var secName in sectionNames)
            {
                foreach (var sec in ts.Elements(secName))
                {
                    var cat = new TilesetCategory { Type = secName };
                    ParseTilesetEntries(sec, cat.Entries);
                    if (cat.Entries.Count > 0) def.Categories.Add(cat);
                }
            }

            // Direct <item> and <brush> children under <tileset> (rare but exists)
            var directCat = new TilesetCategory { Type = "raw" };
            foreach (var item in ts.Elements("item"))
            {
                var fromId = (ushort?)(int?)item.Attribute("fromid") ?? (ushort?)(int?)item.Attribute("id") ?? (ushort)0;
                var toId = (ushort?)(int?)item.Attribute("toid") ?? (ushort)0;
                directCat.Entries.Add(new TilesetEntry
                {
                    Type = "raw",
                    ItemId = fromId,
                    ItemIdEnd = toId,
                    DisplayName = (string?)item.Attribute("name"),
                });
            }
            foreach (var brush in ts.Elements("brush"))
            {
                directCat.Entries.Add(new TilesetEntry
                {
                    Type = "brush",
                    BrushName = (string?)brush.Attribute("name"),
                });
            }
            if (directCat.Entries.Count > 0) def.Categories.Add(directCat);

            if (def.Categories.Count > 0)
                db.Tilesets.Add(def);
        }
    }

    private static void ParseTilesetEntries(XElement section, List<TilesetEntry> entries)
    {
        foreach (var child in section.Elements())
        {
            if (child.Name.LocalName == "item")
            {
                var fromId = (ushort?)(int?)child.Attribute("fromid") ?? (ushort?)(int?)child.Attribute("id") ?? (ushort)0;
                var toId = (ushort?)(int?)child.Attribute("toid") ?? (ushort)0;
                entries.Add(new TilesetEntry
                {
                    Type = "raw",
                    ItemId = fromId,
                    ItemIdEnd = toId,
                    DisplayName = (string?)child.Attribute("name"),
                });
            }
            else if (child.Name.LocalName == "brush")
            {
                entries.Add(new TilesetEntry
                {
                    Type = "brush",
                    BrushName = (string?)child.Attribute("name"),
                });
            }
        }
    }
}
