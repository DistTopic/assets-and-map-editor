using System.Buffers.Binary;
using System.Text;

namespace AssetsAndMapEditor.OTB;

/// <summary>
/// OTBM map file parser. Reads the escape-encoded node tree format
/// used by Tibia map editors (RME, OTAcademy, etc.).
/// </summary>
public static class OtbmFile
{
    // Node markers
    private const byte NODE_START = 0xFE;
    private const byte NODE_END = 0xFF;
    private const byte ESCAPE_CHAR = 0xFD;

    // OTBM node types
    private const byte OTBM_MAP_DATA = 2;
    private const byte OTBM_TILE_AREA = 4;
    private const byte OTBM_TILE = 5;
    private const byte OTBM_ITEM = 6;
    private const byte OTBM_TOWNS = 12;
    private const byte OTBM_TOWN = 13;
    private const byte OTBM_HOUSETILE = 14;
    private const byte OTBM_WAYPOINTS = 15;
    private const byte OTBM_WAYPOINT = 16;

    // OTBM attributes
    private const byte ATTR_DESCRIPTION = 1;
    private const byte ATTR_TILE_FLAGS = 3;
    private const byte ATTR_ACTION_ID = 4;
    private const byte ATTR_UNIQUE_ID = 5;
    private const byte ATTR_TEXT = 6;
    private const byte ATTR_TELE_DEST = 8;
    private const byte ATTR_ITEM = 9;
    private const byte ATTR_DEPOT_ID = 10;
    private const byte ATTR_EXT_SPAWN_FILE = 11;
    private const byte ATTR_EXT_HOUSE_FILE = 13;
    private const byte ATTR_HOUSEDOORID = 14;
    private const byte ATTR_COUNT = 15;
    private const byte ATTR_CHARGES = 22;

    public static MapData Load(string path)
    {
        var raw = File.ReadAllBytes(path);
        return Parse(raw);
    }

    // ════════════════════════════════════════════════════════════
    //  Save
    // ════════════════════════════════════════════════════════════

    public static void Save(string path, MapData map)
    {
        using var ms = new MemoryStream();
        var w = new NodeWriter(ms);

        // 4-byte file identifier (raw, no escaping)
        ms.Write(new byte[] { 0, 0, 0, 0 });

        // Root node (type 0)
        w.AddNode(0);
        w.AddU32(map.Version);
        w.AddU16(map.Width);
        w.AddU16(map.Height);
        w.AddU32(map.OtbMajorVersion);
        w.AddU32(map.OtbMinorVersion);

        // MAP_DATA
        w.AddNode(OTBM_MAP_DATA);
        if (!string.IsNullOrEmpty(map.Description))
        {
            w.AddU8(ATTR_DESCRIPTION);
            w.AddString(map.Description);
        }
        if (!string.IsNullOrEmpty(map.SpawnFile))
        {
            w.AddU8(ATTR_EXT_SPAWN_FILE);
            w.AddString(map.SpawnFile);
        }
        if (!string.IsNullOrEmpty(map.HouseFile))
        {
            w.AddU8(ATTR_EXT_HOUSE_FILE);
            w.AddString(map.HouseFile);
        }

        // Tile areas — group tiles by 256×256 region + Z
        WriteTileAreas(w, map);

        // Towns
        w.AddNode(OTBM_TOWNS);
        foreach (var town in map.Towns)
        {
            w.AddNode(OTBM_TOWN);
            w.AddU32(town.Id);
            w.AddString(town.Name);
            w.AddU16(town.TempleX);
            w.AddU16(town.TempleY);
            w.AddU8(town.TempleZ);
            w.EndNode();
        }
        w.EndNode(); // TOWNS

        // Waypoints
        w.AddNode(OTBM_WAYPOINTS);
        foreach (var wp in map.Waypoints)
        {
            w.AddNode(OTBM_WAYPOINT);
            w.AddString(wp.Name);
            w.AddU16(wp.X);
            w.AddU16(wp.Y);
            w.AddU8(wp.Z);
            w.EndNode();
        }
        w.EndNode(); // WAYPOINTS

        w.EndNode(); // MAP_DATA
        w.EndNode(); // Root

        File.WriteAllBytes(path, ms.ToArray());
    }

    private static void WriteTileAreas(NodeWriter w, MapData map)
    {
        // Sort tiles by z, then by area (x>>8, y>>8), then by position
        var sorted = map.Tiles.Values
            .OrderBy(t => t.Position.Z)
            .ThenBy(t => t.Position.X >> 8)
            .ThenBy(t => t.Position.Y >> 8)
            .ThenBy(t => t.Position.X)
            .ThenBy(t => t.Position.Y);

        int areaX = -1, areaY = -1, areaZ = -1;
        bool areaOpen = false;

        foreach (var tile in sorted)
        {
            var pos = tile.Position;
            int curAreaX = pos.X & 0xFF00;
            int curAreaY = pos.Y & 0xFF00;

            if (curAreaX != areaX || curAreaY != areaY || pos.Z != areaZ)
            {
                if (areaOpen) w.EndNode();
                w.AddNode(OTBM_TILE_AREA);
                w.AddU16((ushort)curAreaX);
                w.AddU16((ushort)curAreaY);
                w.AddU8(pos.Z);
                areaX = curAreaX;
                areaY = curAreaY;
                areaZ = pos.Z;
                areaOpen = true;
            }

            // Tile node
            bool isHouse = tile.HouseId > 0;
            w.AddNode(isHouse ? OTBM_HOUSETILE : OTBM_TILE);
            w.AddU8((byte)(pos.X & 0xFF));
            w.AddU8((byte)(pos.Y & 0xFF));

            if (isHouse)
                w.AddU32(tile.HouseId);
            if (tile.Flags != 0)
            {
                w.AddU8(ATTR_TILE_FLAGS);
                w.AddU32(tile.Flags);
            }

            // Items: first simple ground can be inline ATTR_ITEM, rest are child nodes
            bool firstItem = true;
            foreach (var item in tile.Items)
            {
                if (firstItem && IsSimpleItem(item))
                {
                    w.AddU8(ATTR_ITEM);
                    w.AddU16(item.Id);
                    firstItem = false;
                    continue;
                }
                firstItem = false;
                WriteItemNode(w, item);
            }

            w.EndNode(); // tile
        }

        if (areaOpen) w.EndNode(); // last area
    }

    private static bool IsSimpleItem(MapItem item)
    {
        return item.Count == 0 && item.ActionId == 0 && item.UniqueId == 0
            && item.Text == null && item.TeleportDestination == null
            && item.DepotId == 0 && item.DoorId == 0 && item.Contents.Count == 0;
    }

    private static void WriteItemNode(NodeWriter w, MapItem item)
    {
        w.AddNode(OTBM_ITEM);
        w.AddU16(item.Id);

        if (item.Count != 0) { w.AddU8(ATTR_COUNT); w.AddU8(item.Count); }
        if (item.ActionId != 0) { w.AddU8(ATTR_ACTION_ID); w.AddU16(item.ActionId); }
        if (item.UniqueId != 0) { w.AddU8(ATTR_UNIQUE_ID); w.AddU16(item.UniqueId); }
        if (item.Text != null) { w.AddU8(ATTR_TEXT); w.AddString(item.Text); }
        if (item.TeleportDestination is { } dest)
        {
            w.AddU8(ATTR_TELE_DEST);
            w.AddU16(dest.X);
            w.AddU16(dest.Y);
            w.AddU8(dest.Z);
        }
        if (item.DepotId != 0) { w.AddU8(ATTR_DEPOT_ID); w.AddU16(item.DepotId); }
        if (item.DoorId != 0) { w.AddU8(ATTR_HOUSEDOORID); w.AddU8(item.DoorId); }

        foreach (var child in item.Contents)
            WriteItemNode(w, child);

        w.EndNode();
    }

    // ════════════════════════════════════════════════════════════
    //  NodeWriter — escape-encoded binary writer
    // ════════════════════════════════════════════════════════════

    private sealed class NodeWriter
    {
        private readonly Stream _stream;
        private readonly byte[] _buf = new byte[8];

        public NodeWriter(Stream stream) => _stream = stream;

        public void AddNode(byte type)
        {
            _stream.WriteByte(NODE_START);
            WriteEscaped(type);
        }

        public void EndNode() => _stream.WriteByte(NODE_END);

        public void AddU8(byte v) => WriteEscaped(v);

        public void AddU16(ushort v)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(_buf, v);
            WriteEscaped(_buf.AsSpan(0, 2));
        }

        public void AddU32(uint v)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(_buf, v);
            WriteEscaped(_buf.AsSpan(0, 4));
        }

        public void AddString(string s)
        {
            var bytes = Encoding.Latin1.GetBytes(s);
            AddU16((ushort)bytes.Length);
            WriteEscaped(bytes);
        }

        private void WriteEscaped(byte b)
        {
            if (b == NODE_START || b == NODE_END || b == ESCAPE_CHAR)
                _stream.WriteByte(ESCAPE_CHAR);
            _stream.WriteByte(b);
        }

        private void WriteEscaped(ReadOnlySpan<byte> data)
        {
            foreach (var b in data)
                WriteEscaped(b);
        }
    }

    private static MapData Parse(byte[] raw)
    {
        // Skip 4-byte file identifier
        var nodes = DecodeNodes(raw, 4);
        if (nodes.Count == 0)
            throw new InvalidDataException("OTBM: no root node found");

        var root = nodes[0];
        var r = new NodeReader(root.Data);

        // Root node: skip type byte (0x00)
        r.Skip(1);

        var map = new MapData
        {
            Version = r.U32(),
            Width = r.U16(),
            Height = r.U16(),
            OtbMajorVersion = r.U32(),
            OtbMinorVersion = r.U32(),
        };

        // Parse children of root
        foreach (var child in root.Children)
        {
            var cr = new NodeReader(child.Data);
            byte nodeType = cr.U8();

            if (nodeType == OTBM_MAP_DATA)
                ParseMapData(cr, child, map);
        }

        return map;
    }

    private static void ParseMapData(NodeReader r, OtbmNode node, MapData map)
    {
        // Read attributes
        while (r.Remaining > 0)
        {
            byte attr = r.U8();
            switch (attr)
            {
                case ATTR_DESCRIPTION:
                    map.Description = r.String();
                    break;
                case ATTR_EXT_SPAWN_FILE:
                    map.SpawnFile = r.String();
                    break;
                case ATTR_EXT_HOUSE_FILE:
                    map.HouseFile = r.String();
                    break;
                default:
                    // Unknown attribute — try to skip string
                    if (r.Remaining >= 2)
                    {
                        var len = r.U16();
                        r.Skip(Math.Min(len, r.Remaining));
                    }
                    break;
            }
        }

        // Parse children
        foreach (var child in node.Children)
        {
            var cr = new NodeReader(child.Data);
            byte childType = cr.U8();

            switch (childType)
            {
                case OTBM_TILE_AREA:
                    ParseTileArea(cr, child, map);
                    break;
                case OTBM_TOWNS:
                    ParseTowns(child, map);
                    break;
                case OTBM_WAYPOINTS:
                    ParseWaypoints(child, map);
                    break;
            }
        }
    }

    private static void ParseTileArea(NodeReader r, OtbmNode node, MapData map)
    {
        ushort baseX = r.U16();
        ushort baseY = r.U16();
        byte baseZ = r.U8();

        foreach (var tileNode in node.Children)
        {
            var tr = new NodeReader(tileNode.Data);
            byte tileType = tr.U8();
            if (tileType != OTBM_TILE && tileType != OTBM_HOUSETILE) continue;

            byte offX = tr.U8();
            byte offY = tr.U8();

            var pos = new MapPosition((ushort)(baseX + offX), (ushort)(baseY + offY), baseZ);
            var tile = new MapTile { Position = pos };

            if (tileType == OTBM_HOUSETILE && tr.Remaining >= 4)
                tile.HouseId = tr.U32();

            // Read tile attributes
            while (tr.Remaining > 0)
            {
                byte attr = tr.U8();
                switch (attr)
                {
                    case ATTR_TILE_FLAGS:
                        if (tr.Remaining >= 4) tile.Flags = tr.U32();
                        break;
                    case ATTR_ITEM:
                        if (tr.Remaining >= 2)
                        {
                            ushort itemId = tr.U16();
                            tile.Items.Add(new MapItem { Id = itemId });
                        }
                        break;
                    default:
                        // Skip unknown — bail to avoid corruption
                        goto doneAttrs;
                }
            }
            doneAttrs:

            // Parse child items
            foreach (var itemNode in tileNode.Children)
                ParseItem(itemNode, tile.Items);

            map.Tiles[pos] = tile;
        }
    }

    private static void ParseItem(OtbmNode node, List<MapItem> target)
    {
        var r = new NodeReader(node.Data);
        byte nodeType = r.U8();
        if (nodeType != OTBM_ITEM || r.Remaining < 2) return;

        var item = new MapItem { Id = r.U16() };

        // Read item attributes
        while (r.Remaining > 0)
        {
            byte attr = r.U8();
            switch (attr)
            {
                case ATTR_COUNT:
                    if (r.Remaining >= 1) item.Count = r.U8();
                    break;
                case ATTR_ACTION_ID:
                    if (r.Remaining >= 2) item.ActionId = r.U16();
                    break;
                case ATTR_UNIQUE_ID:
                    if (r.Remaining >= 2) item.UniqueId = r.U16();
                    break;
                case ATTR_TEXT:
                    if (r.Remaining >= 2) item.Text = r.String();
                    break;
                case ATTR_TELE_DEST:
                    if (r.Remaining >= 5)
                    {
                        ushort dx = r.U16();
                        ushort dy = r.U16();
                        byte dz = r.U8();
                        item.TeleportDestination = new MapPosition(dx, dy, dz);
                    }
                    break;
                case ATTR_DEPOT_ID:
                    if (r.Remaining >= 2) item.DepotId = r.U16();
                    break;
                case ATTR_HOUSEDOORID:
                    if (r.Remaining >= 1) item.DoorId = r.U8();
                    break;
                case ATTR_CHARGES:
                    if (r.Remaining >= 2) r.Skip(2); // skip charges
                    break;
                default:
                    // Unknown attribute — try string skip
                    if (r.Remaining >= 2)
                    {
                        int len = r.U16();
                        r.Skip(Math.Min(len, r.Remaining));
                    }
                    break;
            }
        }

        // Container contents
        foreach (var child in node.Children)
            ParseItem(child, item.Contents);

        target.Add(item);
    }

    private static void ParseTowns(OtbmNode node, MapData map)
    {
        foreach (var child in node.Children)
        {
            var r = new NodeReader(child.Data);
            byte nodeType = r.U8();
            if (nodeType != OTBM_TOWN) continue;
            if (r.Remaining < 4) continue;

            var town = new MapTown { Id = r.U32() };
            if (r.Remaining >= 2) town.Name = r.String();
            if (r.Remaining >= 5)
            {
                town.TempleX = r.U16();
                town.TempleY = r.U16();
                town.TempleZ = r.U8();
            }
            map.Towns.Add(town);
        }
    }

    private static void ParseWaypoints(OtbmNode node, MapData map)
    {
        foreach (var child in node.Children)
        {
            var r = new NodeReader(child.Data);
            byte nodeType = r.U8();
            if (nodeType != OTBM_WAYPOINT) continue;

            var wp = new MapWaypoint();
            if (r.Remaining >= 2) wp.Name = r.String();
            if (r.Remaining >= 5)
            {
                wp.X = r.U16();
                wp.Y = r.U16();
                wp.Z = r.U8();
            }
            map.Waypoints.Add(wp);
        }
    }

    // ── Node tree decoding ──

    private sealed class OtbmNode
    {
        public byte[] Data { get; set; } = [];
        public List<OtbmNode> Children { get; } = [];
    }

    /// <summary>
    /// Decode the escape-encoded node tree from raw OTBM bytes.
    /// </summary>
    private static List<OtbmNode> DecodeNodes(byte[] raw, int offset)
    {
        var rootNodes = new List<OtbmNode>();
        var stack = new Stack<OtbmNode>();
        var currentData = new List<byte>();

        int i = offset;
        while (i < raw.Length)
        {
            byte b = raw[i++];
            switch (b)
            {
                case NODE_START:
                {
                    var node = new OtbmNode();
                    if (stack.Count > 0)
                        stack.Peek().Children.Add(node);
                    else
                        rootNodes.Add(node);

                    // Flush any pending data to the parent's node
                    if (stack.Count > 0 && currentData.Count > 0)
                    {
                        // Only set data if node doesn't have it yet
                        if (stack.Peek().Data.Length == 0)
                            stack.Peek().Data = currentData.ToArray();
                        currentData.Clear();
                    }
                    else
                    {
                        currentData.Clear();
                    }

                    stack.Push(node);
                    break;
                }
                case NODE_END:
                {
                    if (stack.Count > 0)
                    {
                        var node = stack.Pop();
                        if (currentData.Count > 0)
                        {
                            if (node.Data.Length == 0)
                                node.Data = currentData.ToArray();
                            currentData.Clear();
                        }
                    }
                    break;
                }
                case ESCAPE_CHAR:
                {
                    if (i < raw.Length)
                        currentData.Add(raw[i++]);
                    break;
                }
                default:
                    currentData.Add(b);
                    break;
            }
        }

        return rootNodes;
    }

    // ── Node data reader ──

    private sealed class NodeReader(byte[] data)
    {
        private int _pos;
        public int Remaining => data.Length - _pos;

        public byte U8() => data[_pos++];

        public ushort U16()
        {
            var v = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(_pos));
            _pos += 2;
            return v;
        }

        public uint U32()
        {
            var v = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(_pos));
            _pos += 4;
            return v;
        }

        public string String()
        {
            ushort len = U16();
            if (len > Remaining) len = (ushort)Remaining;
            var s = Encoding.Latin1.GetString(data, _pos, len);
            _pos += len;
            return s;
        }

        public void Skip(int n) => _pos = Math.Min(_pos + n, data.Length);
    }
}
