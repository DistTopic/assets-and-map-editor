using System.Buffers.Binary;

namespace POriginsItemEditor.OTB;

/// <summary>
/// Reads and writes items.otb files using the same binary tree format
/// as OpenCoreMMO's OtbBinaryTreeBuilder — with full escape handling.
/// </summary>
public static class OtbFile
{
    private const byte Escape = 0xFD;
    private const byte NodeStart = 0xFE;
    private const byte NodeEnd = 0xFF;

    // ── Public API ──────────────────────────────────────────────────────

    public static OtbData Load(string path)
    {
        var raw = File.ReadAllBytes(path);
        return Parse(raw);
    }

    public static void Save(string path, OtbData data)
    {
        var raw = Serialize(data);
        File.WriteAllBytes(path, raw);
    }

    // ── Parsing ─────────────────────────────────────────────────────────

    private static OtbData Parse(byte[] raw)
    {
        var reader = new OtbReader(raw);

        // 4-byte file header (version info)
        var fileHeader = reader.ReadRawBytes(4);

        // Root node: START type_byte data... children... END
        reader.Expect(NodeStart);
        var rootType = reader.ReadEscaped();
        var rootData = reader.ReadNodeData();

        // Children of root = item nodes
        var items = new List<OtbItem>();
        while (reader.Peek() == NodeStart)
        {
            reader.Expect(NodeStart);
            var group = (OtbGroup)reader.ReadEscaped();
            var nodeData = reader.ReadNodeDataBytes();
            var item = ParseItemNode(group, nodeData);
            items.Add(item);
            // ReadNodeDataBytes consumes until NodeEnd
        }

        reader.Expect(NodeEnd); // root end

        return new OtbData
        {
            FileHeader = fileHeader,
            RootType = rootType,
            RootData = rootData,
            Items = items,
        };
    }

    private static OtbItem ParseItemNode(OtbGroup group, byte[] data)
    {
        var stream = new OtbAttrStream(data);
        var item = new OtbItem { Group = group };

        // First 4 logical bytes = flags
        item.Flags = (OtbFlags)stream.ReadUInt32();

        // Attributes
        while (!stream.IsOver)
        {
            var attrType = stream.ReadByte();
            var attrLen = stream.ReadUInt16();

            switch ((OtbAttribute)attrType)
            {
                case OtbAttribute.ServerId:
                    item.ServerId = stream.ReadUInt16();
                    break;
                case OtbAttribute.ClientId:
                    item.ClientId = stream.ReadUInt16();
                    break;
                case OtbAttribute.Speed:
                    item.Speed = stream.ReadUInt16();
                    break;
                case OtbAttribute.Light2:
                    item.LightLevel = stream.ReadUInt16();
                    item.LightColor = stream.ReadUInt16();
                    break;
                case OtbAttribute.TopOrder:
                    item.TopOrder = stream.ReadByte();
                    break;
                case OtbAttribute.WareId:
                    item.WareId = stream.ReadUInt16();
                    break;
                case OtbAttribute.MinimapColor:
                    item.MinimapColor = stream.ReadUInt16();
                    break;
                case OtbAttribute.MaxReadWriteChars:
                    item.MaxReadWriteChars = stream.ReadUInt16();
                    break;
                case OtbAttribute.MaxReadChars:
                    item.MaxReadChars = stream.ReadUInt16();
                    break;
                case OtbAttribute.SpriteHash:
                    item.SpriteHash = stream.ReadBytes(attrLen);
                    break;
                case OtbAttribute.Name:
                    item.Name = System.Text.Encoding.Latin1.GetString(stream.ReadBytes(attrLen));
                    break;
                default:
                    var unknownData = stream.ReadBytes(attrLen);
                    item.UnknownAttributes.Add((attrType, unknownData));
                    break;
            }
        }

        return item;
    }

    // ── Serialization ───────────────────────────────────────────────────

    private static byte[] Serialize(OtbData data)
    {
        var writer = new OtbWriter();

        // File header
        writer.WriteRaw(data.FileHeader);

        // Root node
        writer.WriteByte(NodeStart);
        writer.WriteEscaped(data.RootType);
        writer.WriteEscapedSpan(data.RootData);

        // Item nodes
        foreach (var item in data.Items)
        {
            writer.WriteByte(NodeStart);
            writer.WriteEscaped((byte)item.Group);
            WriteItemNode(writer, item);
            writer.WriteByte(NodeEnd);
        }

        writer.WriteByte(NodeEnd); // root end
        return writer.ToArray();
    }

    private static void WriteItemNode(OtbWriter writer, OtbItem item)
    {
        // Flags (4 bytes)
        writer.WriteEscapedU32((uint)item.Flags);

        // Attributes
        WriteAttrU16(writer, OtbAttribute.ServerId, item.ServerId);
        WriteAttrU16(writer, OtbAttribute.ClientId, item.ClientId);

        if (item.Speed != 0)
            WriteAttrU16(writer, OtbAttribute.Speed, item.Speed);

        if (item.LightLevel != 0 || item.LightColor != 0)
        {
            writer.WriteEscaped((byte)OtbAttribute.Light2);
            writer.WriteEscapedU16(4); // data length
            writer.WriteEscapedU16(item.LightLevel);
            writer.WriteEscapedU16(item.LightColor);
        }

        if (item.TopOrder != 0)
        {
            writer.WriteEscaped((byte)OtbAttribute.TopOrder);
            writer.WriteEscapedU16(1); // data length
            writer.WriteEscaped(item.TopOrder);
        }

        if (item.WareId != 0)
            WriteAttrU16(writer, OtbAttribute.WareId, item.WareId);

        if (item.MinimapColor != 0)
            WriteAttrU16(writer, OtbAttribute.MinimapColor, item.MinimapColor);

        if (item.MaxReadWriteChars != 0)
            WriteAttrU16(writer, OtbAttribute.MaxReadWriteChars, item.MaxReadWriteChars);

        if (item.MaxReadChars != 0)
            WriteAttrU16(writer, OtbAttribute.MaxReadChars, item.MaxReadChars);

        if (item.SpriteHash is { Length: > 0 })
        {
            writer.WriteEscaped((byte)OtbAttribute.SpriteHash);
            writer.WriteEscapedU16((ushort)item.SpriteHash.Length);
            writer.WriteEscapedSpan(item.SpriteHash);
        }

        if (!string.IsNullOrEmpty(item.Name))
        {
            var nameBytes = System.Text.Encoding.Latin1.GetBytes(item.Name);
            writer.WriteEscaped((byte)OtbAttribute.Name);
            writer.WriteEscapedU16((ushort)nameBytes.Length);
            writer.WriteEscapedSpan(nameBytes);
        }

        // Unknown attributes (lossless round-trip)
        foreach (var (type, attrData) in item.UnknownAttributes)
        {
            writer.WriteEscaped(type);
            writer.WriteEscapedU16((ushort)attrData.Length);
            writer.WriteEscapedSpan(attrData);
        }
    }

    private static void WriteAttrU16(OtbWriter writer, OtbAttribute attr, ushort value)
    {
        writer.WriteEscaped((byte)attr);
        writer.WriteEscapedU16(2); // data length
        writer.WriteEscapedU16(value);
    }

    // ── Helper: OtbReader ───────────────────────────────────────────────

    private sealed class OtbReader(byte[] data)
    {
        private int _pos;

        public byte Peek() => data[_pos];
        public bool IsOver => _pos >= data.Length;

        public void Expect(byte b)
        {
            if (data[_pos] != b)
                throw new InvalidDataException($"Expected 0x{b:X2} at pos {_pos}, got 0x{data[_pos]:X2}");
            _pos++;
        }

        public byte ReadEscaped()
        {
            var b = data[_pos++];
            return b == Escape ? data[_pos++] : b;
        }

        public byte[] ReadRawBytes(int count)
        {
            var result = data[_pos..(_pos + count)];
            _pos += count;
            return result;
        }

        /// <summary>Read raw node data bytes (not unescaped) until NodeStart/NodeEnd.</summary>
        public byte[] ReadNodeData()
        {
            var start = _pos;
            while (_pos < data.Length && data[_pos] != NodeStart && data[_pos] != NodeEnd)
            {
                if (data[_pos] == Escape) _pos++; // skip escape + next
                _pos++;
            }
            return data[start.._pos];
        }

        /// <summary>Read node content (unescaped logical bytes) until NodeEnd, consuming the NodeEnd.</summary>
        public byte[] ReadNodeDataBytes()
        {
            var result = new List<byte>();
            while (_pos < data.Length)
            {
                var b = data[_pos];
                if (b == NodeEnd) { _pos++; return [.. result]; }
                if (b == NodeStart)
                {
                    // Skip nested child nodes (shouldn't happen for item nodes, but be safe)
                    SkipNode();
                    continue;
                }
                if (b == Escape) { _pos++; result.Add(data[_pos++]); }
                else { result.Add(data[_pos++]); }
            }
            return [.. result];
        }

        private void SkipNode()
        {
            _pos++; // START
            var depth = 1;
            while (_pos < data.Length && depth > 0)
            {
                var b = data[_pos++];
                if (b == NodeStart) depth++;
                else if (b == NodeEnd) depth--;
                else if (b == Escape) _pos++;
            }
        }
    }

    // ── Helper: OtbAttrStream (over unescaped bytes) ────────────────────

    private sealed class OtbAttrStream(byte[] data)
    {
        private int _pos;
        public bool IsOver => _pos >= data.Length;

        public byte ReadByte() => data[_pos++];

        public ushort ReadUInt16()
        {
            var v = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(_pos));
            _pos += 2;
            return v;
        }

        public uint ReadUInt32()
        {
            var v = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(_pos));
            _pos += 4;
            return v;
        }

        public byte[] ReadBytes(int count)
        {
            var result = data[_pos..(_pos + count)];
            _pos += count;
            return result;
        }
    }

    // ── Helper: OtbWriter ───────────────────────────────────────────────

    private sealed class OtbWriter
    {
        private readonly List<byte> _buf = [];

        public void WriteByte(byte b) => _buf.Add(b);
        public void WriteRaw(byte[] data) => _buf.AddRange(data);

        public void WriteEscaped(byte b)
        {
            if (b is Escape or NodeStart or NodeEnd)
                _buf.Add(Escape);
            _buf.Add(b);
        }

        public void WriteEscapedU16(ushort value)
        {
            WriteEscaped((byte)(value & 0xFF));
            WriteEscaped((byte)((value >> 8) & 0xFF));
        }

        public void WriteEscapedU32(uint value)
        {
            WriteEscaped((byte)(value & 0xFF));
            WriteEscaped((byte)((value >> 8) & 0xFF));
            WriteEscaped((byte)((value >> 16) & 0xFF));
            WriteEscaped((byte)((value >> 24) & 0xFF));
        }

        public void WriteEscapedSpan(byte[] data)
        {
            foreach (var b in data)
                WriteEscaped(b);
        }

        public byte[] ToArray() => [.. _buf];
    }
}
