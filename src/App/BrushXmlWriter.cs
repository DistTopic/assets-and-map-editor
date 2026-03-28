using System.Xml.Linq;

namespace AssetsAndMapEditor.App;

/// <summary>
/// Writes the BrushCatalog back to standard Tibia editor XML format.
/// </summary>
public static class BrushXmlWriter
{
    public static void SaveAll(BrushCatalog catalog, string dir)
    {
        SaveBorders(catalog, Path.Combine(dir, "borders.xml"));
        SaveGrounds(catalog, Path.Combine(dir, "grounds.xml"));
        SaveWalls(catalog, Path.Combine(dir, "walls.xml"));
        SaveDoodads(catalog, Path.Combine(dir, "doodads.xml"));
        SaveCreatures(catalog, Path.Combine(dir, "creatures.xml"));
        SaveTilesets(catalog, Path.Combine(dir, "tilesets.xml"));
    }

    private static void SaveBorders(BrushCatalog catalog, string path)
    {
        var root = new XElement("materials");
        foreach (var b in catalog.Borders.Values.OrderBy(b => b.Id))
        {
            var el = new XElement("border", new XAttribute("id", b.Id));
            if (b.Group != 0) el.Add(new XAttribute("group", b.Group));
            if (b.Optional) el.Add(new XAttribute("type", "optional"));
            foreach (var (edge, itemId) in b.Edges.OrderBy(e => EdgeOrder(e.Key)))
                el.Add(new XElement("borderitem", new XAttribute("edge", edge), new XAttribute("item", itemId)));
            root.Add(el);
        }
        new XDocument(root).Save(path);
    }

    private static int EdgeOrder(string edge) => edge switch
    {
        "n" => 0, "e" => 1, "s" => 2, "w" => 3,
        "cnw" => 4, "cne" => 5, "csw" => 6, "cse" => 7,
        "dnw" => 8, "dne" => 9, "dsw" => 10, "dse" => 11,
        _ => 99,
    };

    private static void SaveGrounds(BrushCatalog catalog, string path)
    {
        var root = new XElement("materials");
        foreach (var g in catalog.Grounds)
        {
            var el = new XElement("brush",
                new XAttribute("name", g.Name),
                new XAttribute("type", "ground"));
            if (g.LookId > 0) el.Add(new XAttribute("server_lookid", g.LookId));
            if (g.ZOrder != 0) el.Add(new XAttribute("z-order", g.ZOrder));
            if (!g.Randomize) el.Add(new XAttribute("randomize", "false"));

            foreach (var item in g.Items)
                el.Add(new XElement("item", new XAttribute("id", item.Id), new XAttribute("chance", item.Chance)));

            foreach (var border in g.Borders)
            {
                var bEl = new XElement("border",
                    new XAttribute("align", border.Align),
                    new XAttribute("id", border.BorderId));
                if (border.To != null) bEl.Add(new XAttribute("to", border.To));
                if (border.Super) bEl.Add(new XAttribute("super", "true"));
                el.Add(bEl);
            }

            foreach (var f in g.Friends)
                el.Add(new XElement("friend", new XAttribute("name", f)));

            root.Add(el);
        }
        new XDocument(root).Save(path);
    }

    private static void SaveWalls(BrushCatalog catalog, string path)
    {
        var root = new XElement("materials");
        foreach (var w in catalog.Walls)
        {
            var el = new XElement("brush",
                new XAttribute("name", w.Name),
                new XAttribute("type", "wall"));
            if (w.LookId > 0) el.Add(new XAttribute("server_lookid", w.LookId));

            foreach (var (segType, seg) in w.Segments)
            {
                var wEl = new XElement("wall", new XAttribute("type", segType));
                foreach (var item in seg.Items)
                    wEl.Add(new XElement("item", new XAttribute("id", item.Id), new XAttribute("chance", item.Chance)));
                foreach (var door in seg.Doors)
                {
                    var dEl = new XElement("door",
                        new XAttribute("id", door.Id),
                        new XAttribute("type", door.Type));
                    if (door.Open) dEl.Add(new XAttribute("open", "true"));
                    if (door.Locked) dEl.Add(new XAttribute("locked", "true"));
                    wEl.Add(dEl);
                }
                el.Add(wEl);
            }
            root.Add(el);
        }
        new XDocument(root).Save(path);
    }

    private static void SaveDoodads(BrushCatalog catalog, string path)
    {
        var root = new XElement("materials");
        foreach (var d in catalog.Doodads)
        {
            var el = new XElement("brush",
                new XAttribute("name", d.Name),
                new XAttribute("type", "doodad"));
            if (d.LookId > 0) el.Add(new XAttribute("server_lookid", d.LookId));
            if (d.Draggable) el.Add(new XAttribute("draggable", "true"));
            if (d.OnBlocking) el.Add(new XAttribute("on_blocking", "true"));
            if (d.OnDuplicate) el.Add(new XAttribute("on_duplicate", "true"));
            if (d.RedoBorders) el.Add(new XAttribute("redo_borders", "true"));
            if (d.ThicknessNum != 1 || d.ThicknessDen != 1)
                el.Add(new XAttribute("thickness", $"{d.ThicknessNum}/{d.ThicknessDen}"));

            foreach (var item in d.Items)
                el.Add(new XElement("item", new XAttribute("id", item.Id), new XAttribute("chance", item.Chance)));

            foreach (var comp in d.Composites)
                el.Add(WriteComposite(comp));

            foreach (var alt in d.Alternates)
            {
                var altEl = new XElement("alternate");
                foreach (var item in alt.Items)
                    altEl.Add(new XElement("item", new XAttribute("id", item.Id), new XAttribute("chance", item.Chance)));
                foreach (var comp in alt.Composites)
                    altEl.Add(WriteComposite(comp));
                el.Add(altEl);
            }

            root.Add(el);
        }
        new XDocument(root).Save(path);
    }

    private static XElement WriteComposite(DoodadComposite comp)
    {
        var cEl = new XElement("composite");
        if (comp.Chance > 0) cEl.Add(new XAttribute("chance", comp.Chance));
        foreach (var tile in comp.Tiles)
        {
            var tEl = new XElement("tile",
                new XAttribute("x", tile.X),
                new XAttribute("y", tile.Y));
            foreach (var id in tile.Items)
                tEl.Add(new XElement("item", new XAttribute("id", id)));
            cEl.Add(tEl);
        }
        return cEl;
    }

    private static void SaveCreatures(BrushCatalog catalog, string path)
    {
        var root = new XElement("creatures");
        foreach (var c in catalog.Creatures.OrderBy(c => c.Name))
        {
            var el = new XElement("creature",
                new XAttribute("name", c.Name),
                new XAttribute("type", c.Type));
            if (c.LookType > 0) el.Add(new XAttribute("looktype", c.LookType));
            if (c.LookItem > 0) el.Add(new XAttribute("lookitem", c.LookItem));
            if (c.LookHead > 0) el.Add(new XAttribute("lookhead", c.LookHead));
            if (c.LookBody > 0) el.Add(new XAttribute("lookbody", c.LookBody));
            if (c.LookLegs > 0) el.Add(new XAttribute("looklegs", c.LookLegs));
            if (c.LookFeet > 0) el.Add(new XAttribute("lookfeet", c.LookFeet));
            root.Add(el);
        }
        new XDocument(new XDeclaration("1.0", "UTF-8", null), root).Save(path);
    }

    private static void SaveTilesets(BrushCatalog catalog, string path)
    {
        var root = new XElement("materials");
        foreach (var ts in catalog.Tilesets)
        {
            var tsEl = new XElement("tileset", new XAttribute("name", ts.Name));
            foreach (var cat in ts.Categories)
            {
                var secEl = new XElement(cat.Type);
                foreach (var entry in cat.Entries)
                {
                    if (entry.Type == "brush" && entry.BrushName != null)
                        secEl.Add(new XElement("brush", new XAttribute("name", entry.BrushName)));
                    else if (entry.ItemIdEnd > 0)
                        secEl.Add(new XElement("item",
                            new XAttribute("fromid", entry.ItemId),
                            new XAttribute("toid", entry.ItemIdEnd)));
                    else
                    {
                        var itemEl = new XElement("item", new XAttribute("id", entry.ItemId));
                        if (entry.DisplayName != null) itemEl.Add(new XAttribute("name", entry.DisplayName));
                        secEl.Add(itemEl);
                    }
                }
                tsEl.Add(secEl);
            }
            root.Add(tsEl);
        }
        new XDocument(root).Save(path);
    }
}
