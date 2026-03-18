using System.Buffers.Binary;
using System.Text;

namespace POriginsItemEditor.OTB;

/// <summary>
/// Tibia.dat parser for protocol 1098 (MetadataFlags6).
/// Parses all thing types (items, outfits, effects, missiles) with full structure.
/// </summary>
public static class DatFile
{
    public static DatData Load(string path)
    {
        var raw = File.ReadAllBytes(path);
        return Parse(raw);
    }

    private static DatData Parse(byte[] raw)
    {
        var r = new DatReader(raw);

        var signature = r.U32();
        var lastItemId = r.U16();     // header stores LAST ID, not count
        var lastOutfitId = r.U16();
        var lastEffectId = r.U16();
        var lastMissileId = r.U16();

        int numItems = lastItemId - 100 + 1;     // items start at 100
        int numOutfits = lastOutfitId;            // outfits start at 1
        int numEffects = lastEffectId;            // effects start at 1
        int numMissiles = lastMissileId;          // missiles start at 1

        var items = new Dictionary<ushort, DatThingType>(numItems);
        var outfits = new Dictionary<ushort, DatThingType>(numOutfits);
        var effects = new Dictionary<ushort, DatThingType>(numEffects);
        var missiles = new Dictionary<ushort, DatThingType>(numMissiles);

        // Items: 100..lastItemId (inclusive)
        for (int id = 100; id <= lastItemId; id++)
        {
            var thing = ParseThing(r, (ushort)id, ThingCategory.Item);
            items[(ushort)id] = thing;
        }

        // Parse outfits/effects/missiles independently — don't let failures block items
        try
        {
            for (int id = 1; id <= numOutfits; id++)
                outfits[(ushort)id] = ParseThing(r, (ushort)id, ThingCategory.Outfit);

            for (int id = 1; id <= numEffects; id++)
                effects[(ushort)id] = ParseThing(r, (ushort)id, ThingCategory.Effect);

            for (int id = 1; id <= numMissiles; id++)
                missiles[(ushort)id] = ParseThing(r, (ushort)id, ThingCategory.Missile);
        }
        catch
        {
            // Non-item categories may fail on certain DAT variants; items are still valid
        }

        return new DatData
        {
            Signature = signature,
            ItemCount = (ushort)numItems,
            OutfitCount = (ushort)numOutfits,
            EffectCount = (ushort)numEffects,
            MissileCount = (ushort)numMissiles,
            Items = items,
            Outfits = outfits,
            Effects = effects,
            Missiles = missiles,
        };
    }

    private static DatThingType ParseThing(DatReader r, ushort id, ThingCategory category)
    {
        var thing = new DatThingType { Id = id, Category = category };

        // ── Parse flags (MetadataFlags6) ──
        ParseFlags(r, thing);

        // ── Parse frame groups ──
        bool isOutfit = category == ThingCategory.Outfit;
        int groupCount = isOutfit ? r.U8() : 1;
        var groups = new FrameGroup[groupCount];

        for (int g = 0; g < groupCount; g++)
        {
            var fg = new FrameGroup();
            if (isOutfit)
                fg.Type = (FrameGroupType)r.U8();

            fg.Width = r.U8();
            fg.Height = r.U8();
            if (fg.Width > 1 || fg.Height > 1)
                fg.ExactSize = r.U8();

            fg.Layers = r.U8();
            fg.PatternX = r.U8();
            fg.PatternY = r.U8();
            fg.PatternZ = r.U8();
            fg.Frames = r.U8();

            // Improved animations (protocol >= 1050, active for 1098)
            if (fg.Frames > 1)
            {
                fg.AnimationMode = (AnimationMode)r.U8();
                fg.LoopCount = r.S32();
                fg.StartFrame = r.S8();
                fg.FrameDurations = new FrameDuration[fg.Frames];
                for (int i = 0; i < fg.Frames; i++)
                {
                    fg.FrameDurations[i] = new FrameDuration
                    {
                        Minimum = r.U32(),
                        Maximum = r.U32(),
                    };
                }
            }

            // Sprite index (extended = U32 for protocol >= 960)
            int totalSprites = fg.SpriteCount;
            fg.SpriteIndex = new uint[totalSprites];
            for (int i = 0; i < totalSprites; i++)
                fg.SpriteIndex[i] = r.U32();

            groups[g] = fg;
        }

        thing.FrameGroups = groups;
        return thing;
    }

    private static void ParseFlags(DatReader r, DatThingType thing)
    {
        while (true)
        {
            int flag = r.U8();
            if (flag == 0xFF) break;

            switch (flag)
            {
                case 0x00: // Ground
                    thing.IsGround = true;
                    thing.GroundSpeed = r.U16();
                    break;
                case 0x01: thing.IsGroundBorder = true; break;
                case 0x02: thing.IsOnBottom = true; break;
                case 0x03: thing.IsOnTop = true; break;
                case 0x04: thing.IsContainer = true; break;
                case 0x05: thing.IsStackable = true; break;
                case 0x06: thing.IsForceUse = true; break;
                case 0x07: thing.IsMultiUse = true; break;
                case 0x08: // Writable
                    thing.IsWritable = true;
                    thing.MaxTextLength = r.U16();
                    break;
                case 0x09: // Writable once
                    thing.IsWritableOnce = true;
                    thing.MaxTextLength = r.U16();
                    break;
                case 0x0A: thing.IsFluidContainer = true; break;
                case 0x0B: thing.IsFluid = true; break;
                case 0x0C: thing.IsUnpassable = true; break;
                case 0x0D: thing.IsUnmoveable = true; break;
                case 0x0E: thing.IsBlockMissile = true; break;
                case 0x0F: thing.IsBlockPathfind = true; break;
                case 0x10: thing.IsNoMoveAnimation = true; break;
                case 0x11: thing.IsPickupable = true; break;
                case 0x12: thing.IsHangable = true; break;
                case 0x13: thing.IsVertical = true; break;
                case 0x14: thing.IsHorizontal = true; break;
                case 0x15: thing.IsRotatable = true; break;
                case 0x16: // Has light
                    thing.HasLight = true;
                    thing.LightLevel = r.U16();
                    thing.LightColor = r.U16();
                    break;
                case 0x17: thing.IsDontHide = true; break;
                case 0x18: thing.IsTranslucent = true; break;
                case 0x19: // Has offset
                    thing.HasOffset = true;
                    thing.OffsetX = r.S16();
                    thing.OffsetY = r.S16();
                    break;
                case 0x1A: // Has elevation
                    thing.HasElevation = true;
                    thing.Elevation = r.U16();
                    break;
                case 0x1B: thing.IsLyingObject = true; break;
                case 0x1C: thing.IsAnimateAlways = true; break;
                case 0x1D: // Minimap
                    thing.IsMiniMap = true;
                    thing.MiniMapColor = r.U16();
                    break;
                case 0x1E: // Lens help
                    thing.IsLensHelp = true;
                    thing.LensHelp = r.U16();
                    break;
                case 0x1F: thing.IsFullGround = true; break;
                case 0x20: thing.IsIgnoreLook = true; break;
                case 0x21: // Cloth
                    thing.IsCloth = true;
                    thing.ClothSlot = r.U16();
                    break;
                case 0x22: // Market item
                    thing.IsMarketItem = true;
                    thing.MarketCategory = r.U16();
                    thing.MarketTradeAs = r.U16();
                    thing.MarketShowAs = r.U16();
                    var nameLen = r.U16();
                    if (nameLen > 512) nameLen = 512;
                    var rawName = r.String(nameLen);
                    // Keep only printable ASCII (valid market names are plain text)
                    thing.MarketName = new string(rawName.Where(c => c >= 0x20 && c <= 0x7E).ToArray());
                    thing.MarketRestrictProfession = r.U16();
                    thing.MarketRestrictLevel = r.U16();
                    break;
                case 0x23: // Default action
                    thing.HasDefaultAction = true;
                    thing.DefaultAction = r.U16();
                    break;
                case 0x24: thing.IsWrappable = true; break;
                case 0x25: thing.IsUnwrappable = true; break;
                case 0x26: thing.IsTopEffect = true; break;
                case 0xFE: thing.IsUsable = true; break;
            }
        }
    }

    public static void Save(string path, DatData data)
    {
        var w = new DatWriter();

        // Header: signature + last IDs
        ushort lastItemId = data.Items.Count > 0 ? data.Items.Keys.Max() : (ushort)99;
        ushort lastOutfitId = data.Outfits.Count > 0 ? data.Outfits.Keys.Max() : (ushort)0;
        ushort lastEffectId = data.Effects.Count > 0 ? data.Effects.Keys.Max() : (ushort)0;
        ushort lastMissileId = data.Missiles.Count > 0 ? data.Missiles.Keys.Max() : (ushort)0;

        w.U32(data.Signature);
        w.U16(lastItemId);
        w.U16(lastOutfitId);
        w.U16(lastEffectId);
        w.U16(lastMissileId);

        // Items: 100..lastItemId
        for (int id = 100; id <= lastItemId; id++)
        {
            if (data.Items.TryGetValue((ushort)id, out var thing))
                WriteThing(w, thing);
            else
                WriteEmptyThing(w);
        }

        // Outfits: 1..lastOutfitId
        for (int id = 1; id <= lastOutfitId; id++)
        {
            if (data.Outfits.TryGetValue((ushort)id, out var thing))
                WriteThing(w, thing);
            else
                WriteEmptyThing(w);
        }

        // Effects: 1..lastEffectId
        for (int id = 1; id <= lastEffectId; id++)
        {
            if (data.Effects.TryGetValue((ushort)id, out var thing))
                WriteThing(w, thing);
            else
                WriteEmptyThing(w);
        }

        // Missiles: 1..lastMissileId
        for (int id = 1; id <= lastMissileId; id++)
        {
            if (data.Missiles.TryGetValue((ushort)id, out var thing))
                WriteThing(w, thing);
            else
                WriteEmptyThing(w);
        }

        File.WriteAllBytes(path, w.ToArray());
    }

    private static void WriteEmptyThing(DatWriter w)
    {
        w.U8(0xFF); // end flags
        // 1 frame group: 1x1, exactSize=32, 1 layer, 1x1x1 pattern, 1 frame, 1 sprite (id=0)
        w.U8(1); w.U8(1); // width, height
        w.U8(1); // layers
        w.U8(1); w.U8(1); w.U8(1); // patternX/Y/Z
        w.U8(1); // frames
        w.U32(0); // sprite id
    }

    private static void WriteThing(DatWriter w, DatThingType thing)
    {
        WriteFlags(w, thing);

        bool isOutfit = thing.Category == ThingCategory.Outfit;
        if (isOutfit)
            w.U8((byte)thing.FrameGroups.Length);

        for (int g = 0; g < thing.FrameGroups.Length; g++)
        {
            var fg = thing.FrameGroups[g];
            if (isOutfit)
                w.U8((byte)fg.Type);

            w.U8(fg.Width);
            w.U8(fg.Height);
            if (fg.Width > 1 || fg.Height > 1)
                w.U8(fg.ExactSize);

            w.U8(fg.Layers);
            w.U8(fg.PatternX);
            w.U8(fg.PatternY);
            w.U8(fg.PatternZ);
            w.U8(fg.Frames);

            if (fg.Frames > 1)
            {
                w.U8((byte)fg.AnimationMode);
                w.S32(fg.LoopCount);
                w.S8(fg.StartFrame);
                for (int i = 0; i < fg.Frames; i++)
                {
                    var dur = i < fg.FrameDurations.Length
                        ? fg.FrameDurations[i]
                        : new FrameDuration { Minimum = 100, Maximum = 100 };
                    w.U32(dur.Minimum);
                    w.U32(dur.Maximum);
                }
            }

            int totalSprites = fg.SpriteCount;
            for (int i = 0; i < totalSprites; i++)
                w.U32(i < fg.SpriteIndex.Length ? fg.SpriteIndex[i] : 0);
        }
    }

    private static void WriteFlags(DatWriter w, DatThingType t)
    {
        if (t.IsGround) { w.U8(0x00); w.U16(t.GroundSpeed); }
        if (t.IsGroundBorder) w.U8(0x01);
        if (t.IsOnBottom) w.U8(0x02);
        if (t.IsOnTop) w.U8(0x03);
        if (t.IsContainer) w.U8(0x04);
        if (t.IsStackable) w.U8(0x05);
        if (t.IsForceUse) w.U8(0x06);
        if (t.IsMultiUse) w.U8(0x07);
        if (t.IsWritable) { w.U8(0x08); w.U16(t.MaxTextLength); }
        if (t.IsWritableOnce) { w.U8(0x09); w.U16(t.MaxTextLength); }
        if (t.IsFluidContainer) w.U8(0x0A);
        if (t.IsFluid) w.U8(0x0B);
        if (t.IsUnpassable) w.U8(0x0C);
        if (t.IsUnmoveable) w.U8(0x0D);
        if (t.IsBlockMissile) w.U8(0x0E);
        if (t.IsBlockPathfind) w.U8(0x0F);
        if (t.IsNoMoveAnimation) w.U8(0x10);
        if (t.IsPickupable) w.U8(0x11);
        if (t.IsHangable) w.U8(0x12);
        if (t.IsVertical) w.U8(0x13);
        if (t.IsHorizontal) w.U8(0x14);
        if (t.IsRotatable) w.U8(0x15);
        if (t.HasLight) { w.U8(0x16); w.U16(t.LightLevel); w.U16(t.LightColor); }
        if (t.IsDontHide) w.U8(0x17);
        if (t.IsTranslucent) w.U8(0x18);
        if (t.HasOffset) { w.U8(0x19); w.S16(t.OffsetX); w.S16(t.OffsetY); }
        if (t.HasElevation) { w.U8(0x1A); w.U16(t.Elevation); }
        if (t.IsLyingObject) w.U8(0x1B);
        if (t.IsAnimateAlways) w.U8(0x1C);
        if (t.IsMiniMap) { w.U8(0x1D); w.U16(t.MiniMapColor); }
        if (t.IsLensHelp) { w.U8(0x1E); w.U16(t.LensHelp); }
        if (t.IsFullGround) w.U8(0x1F);
        if (t.IsIgnoreLook) w.U8(0x20);
        if (t.IsCloth) { w.U8(0x21); w.U16(t.ClothSlot); }
        if (t.IsMarketItem)
        {
            w.U8(0x22);
            w.U16(t.MarketCategory);
            w.U16(t.MarketTradeAs);
            w.U16(t.MarketShowAs);
            var nameBytes = Encoding.Latin1.GetBytes(t.MarketName ?? string.Empty);
            w.U16((ushort)nameBytes.Length);
            w.Bytes(nameBytes);
            w.U16(t.MarketRestrictProfession);
            w.U16(t.MarketRestrictLevel);
        }
        if (t.HasDefaultAction) { w.U8(0x23); w.U16(t.DefaultAction); }
        if (t.IsWrappable) w.U8(0x24);
        if (t.IsUnwrappable) w.U8(0x25);
        if (t.IsTopEffect) w.U8(0x26);
        if (t.IsUsable) w.U8(0xFE);
        w.U8(0xFF); // end marker
    }

    internal sealed class DatWriter
    {
        private readonly MemoryStream _ms = new();

        public void U8(byte v) => _ms.WriteByte(v);
        public void S8(sbyte v) => _ms.WriteByte((byte)v);

        public void U16(ushort v)
        {
            Span<byte> buf = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(buf, v);
            _ms.Write(buf);
        }

        public void S16(short v)
        {
            Span<byte> buf = stackalloc byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(buf, v);
            _ms.Write(buf);
        }

        public void U32(uint v)
        {
            Span<byte> buf = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(buf, v);
            _ms.Write(buf);
        }

        public void S32(int v)
        {
            Span<byte> buf = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buf, v);
            _ms.Write(buf);
        }

        public void Bytes(byte[] data) => _ms.Write(data);

        public byte[] ToArray() => _ms.ToArray();
    }

    internal sealed class DatReader(byte[] data)
    {
        private int _pos;

        public int Remaining => data.Length - _pos;

        public byte U8() => data[_pos++];

        public sbyte S8()
        {
            var v = (sbyte)data[_pos];
            _pos++;
            return v;
        }

        public ushort U16()
        {
            var v = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(_pos));
            _pos += 2;
            return v;
        }

        public short S16()
        {
            var v = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(_pos));
            _pos += 2;
            return v;
        }

        public uint U32()
        {
            var v = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(_pos));
            _pos += 4;
            return v;
        }

        public int S32()
        {
            var v = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(_pos));
            _pos += 4;
            return v;
        }

        public string String(int length)
        {
            var s = Encoding.Latin1.GetString(data, _pos, length);
            _pos += length;
            return s;
        }

        public void Skip(int n) => _pos += n;
    }
}

public sealed class DatData
{
    public uint Signature { get; init; }
    public ushort ItemCount { get; init; }
    public ushort OutfitCount { get; init; }
    public ushort EffectCount { get; init; }
    public ushort MissileCount { get; init; }

    /// <summary>Client-ID → full thing type for items.</summary>
    public Dictionary<ushort, DatThingType> Items { get; init; } = [];
    public Dictionary<ushort, DatThingType> Outfits { get; init; } = [];
    public Dictionary<ushort, DatThingType> Effects { get; init; } = [];
    public Dictionary<ushort, DatThingType> Missiles { get; init; } = [];
}

// Keep backward compat alias
public sealed class DatItemInfo
{
    public int AnimPhases { get; init; }
    public bool AnimateAlways { get; init; }
    public uint FirstSpriteId { get; init; }
}
