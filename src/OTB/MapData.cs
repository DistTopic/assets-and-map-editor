namespace AssetsAndMapEditor.OTB;

/// <summary>
/// Represents a loaded OTBM map with all tiles, towns, and waypoints.
/// </summary>
public sealed class MapData
{
    public uint Version { get; set; }
    public ushort Width { get; set; }
    public ushort Height { get; set; }
    public uint OtbMajorVersion { get; set; }
    public uint OtbMinorVersion { get; set; }
    public string Description { get; set; } = string.Empty;
    public string SpawnFile { get; set; } = string.Empty;
    public string HouseFile { get; set; } = string.Empty;

    /// <summary>Tiles indexed by (x, y, z) position.</summary>
    public Dictionary<MapPosition, MapTile> Tiles { get; } = [];

    public List<MapTown> Towns { get; } = [];
    public List<MapWaypoint> Waypoints { get; } = [];

    /// <summary>Get the bounding box of all tiles.</summary>
    public (int minX, int minY, int maxX, int maxY) GetBounds()
    {
        if (Tiles.Count == 0) return (0, 0, 0, 0);
        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;
        foreach (var pos in Tiles.Keys)
        {
            if (pos.X < minX) minX = pos.X;
            if (pos.Y < minY) minY = pos.Y;
            if (pos.X > maxX) maxX = pos.X;
            if (pos.Y > maxY) maxY = pos.Y;
        }
        return (minX, minY, maxX, maxY);
    }

    /// <summary>Get all distinct Z levels present in the map.</summary>
    public byte[] GetFloors()
    {
        var floors = new HashSet<byte>();
        foreach (var pos in Tiles.Keys)
            floors.Add(pos.Z);
        var arr = floors.ToArray();
        Array.Sort(arr);
        return arr;
    }
}

/// <summary>3D tile position.</summary>
public readonly record struct MapPosition(ushort X, ushort Y, byte Z);

/// <summary>A single map tile with ground item and item stack.</summary>
public sealed class MapTile
{
    public MapPosition Position { get; init; }
    public uint Flags { get; set; }
    public uint HouseId { get; set; }

    /// <summary>All items on this tile, in draw order (ground first).</summary>
    public List<MapItem> Items { get; } = [];

    /// <summary>The ground item (first item, or null).</summary>
    public MapItem? Ground => Items.Count > 0 ? Items[0] : null;

    /// <summary>Deep-clone this tile (new MapTile with copied items).</summary>
    public MapTile Clone()
    {
        var clone = new MapTile { Position = Position, Flags = Flags, HouseId = HouseId };
        foreach (var item in Items) clone.Items.Add(item.Clone());
        return clone;
    }
}

/// <summary>An item placed on a map tile.</summary>
public sealed class MapItem
{
    public ushort Id { get; set; }
    public byte Count { get; set; }
    public ushort ActionId { get; set; }
    public ushort UniqueId { get; set; }
    public string? Text { get; set; }
    public MapPosition? TeleportDestination { get; set; }
    public ushort DepotId { get; set; }
    public byte DoorId { get; set; }

    /// <summary>Items inside (if this is a container).</summary>
    public List<MapItem> Contents { get; } = [];

    /// <summary>Deep-clone this item (including contents).</summary>
    public MapItem Clone()
    {
        var clone = new MapItem
        {
            Id = Id, Count = Count, ActionId = ActionId, UniqueId = UniqueId,
            Text = Text, TeleportDestination = TeleportDestination,
            DepotId = DepotId, DoorId = DoorId
        };
        foreach (var c in Contents) clone.Contents.Add(c.Clone());
        return clone;
    }
}

/// <summary>A town defined in the map.</summary>
public sealed class MapTown
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ushort TempleX { get; set; }
    public ushort TempleY { get; set; }
    public byte TempleZ { get; set; }
}

/// <summary>A waypoint defined in the map.</summary>
public sealed class MapWaypoint
{
    public string Name { get; set; } = string.Empty;
    public ushort X { get; set; }
    public ushort Y { get; set; }
    public byte Z { get; set; }
}
