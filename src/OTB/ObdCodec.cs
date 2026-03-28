using System.Buffers.Binary;
using System.Text;
using SharpCompress.Compressors.LZMA;

namespace AssetsAndMapEditor.OTB;

/// <summary>
/// Encodes and decodes Object Builder Data (.obd) files.
/// Supports OBD V1 (client ≥ 710), V2 (version 200), V3 (version 300).
/// Binary compatible with Object Builder's OBDEncoder.
/// </summary>
public static class ObdCodec
{
    public const ushort OBD_V1 = 100;
    public const ushort OBD_V2 = 200;
    public const ushort OBD_V3 = 300;

    /// <summary>Result of decoding an .obd file.</summary>
    public sealed class ObdData
    {
        public required ushort ObdVersion { get; init; }
        public required ushort ClientVersion { get; init; }
        public required ThingCategory Category { get; init; }
        public required DatThingType Thing { get; init; }
        /// <summary>Sprite ID → raw RGBA pixel data (32×32 = 4096 bytes each).</summary>
        public required Dictionary<uint, byte[]> Sprites { get; init; }
    }

    // ── Flag constants (same as OB's OBDEncoder) ──

    private const byte GROUND = 0x00;
    private const byte GROUND_BORDER = 0x01;
    private const byte ON_BOTTOM = 0x02;
    private const byte ON_TOP = 0x03;
    private const byte CONTAINER = 0x04;
    private const byte STACKABLE = 0x05;
    private const byte FORCE_USE = 0x06;
    private const byte MULTI_USE = 0x07;
    private const byte WRITABLE = 0x08;
    private const byte WRITABLE_ONCE = 0x09;
    private const byte FLUID_CONTAINER = 0x0A;
    private const byte FLUID = 0x0B;
    private const byte UNPASSABLE = 0x0C;
    private const byte UNMOVEABLE = 0x0D;
    private const byte BLOCK_MISSILE = 0x0E;
    private const byte BLOCK_PATHFIND = 0x0F;
    private const byte NO_MOVE_ANIMATION = 0x10;
    private const byte PICKUPABLE = 0x11;
    private const byte HANGABLE = 0x12;
    private const byte HOOK_SOUTH = 0x13;
    private const byte HOOK_EAST = 0x14;
    private const byte ROTATABLE = 0x15;
    private const byte HAS_LIGHT = 0x16;
    private const byte DONT_HIDE = 0x17;
    private const byte TRANSLUCENT = 0x18;
    private const byte HAS_OFFSET = 0x19;
    private const byte HAS_ELEVATION = 0x1A;
    private const byte LYING_OBJECT = 0x1B;
    private const byte ANIMATE_ALWAYS = 0x1C;
    private const byte MINI_MAP = 0x1D;
    private const byte LENS_HELP = 0x1E;
    private const byte FULL_GROUND = 0x1F;
    private const byte IGNORE_LOOK = 0x20;
    private const byte CLOTH = 0x21;
    private const byte MARKET_ITEM = 0x22;
    private const byte DEFAULT_ACTION = 0x23;
    private const byte WRAPPABLE = 0x24;
    private const byte UNWRAPPABLE = 0x25;
    private const byte TOP_EFFECT = 0x26;
    private const byte HAS_CHARGES = 0xFC;
    private const byte FLOOR_CHANGE = 0xFD;
    private const byte USABLE = 0xFE;
    private const byte LAST_FLAG = 0xFF;

    private const int SPRITE_DATA_SIZE = 4096; // 32×32 RGBA

    // ═══════════════════════════════════════════════════════════════
    //  DECODE
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Decode an .obd file from raw bytes.</summary>
    public static ObdData Decode(byte[] fileBytes)
    {
        var decompressed = LzmaDecompress(fileBytes);
        var r = new DatReader(decompressed);

        ushort version = r.U16();
        if (version == OBD_V3) return DecodeV3(r);
        if (version == OBD_V2) return DecodeV2(r);
        if (version >= 710) return DecodeV1(r, version);
        throw new InvalidDataException($"Invalid OBD version: {version}");
    }

    private static ObdData DecodeV1(DatReader r, ushort clientVersion)
    {
        // V1: first 2 bytes were clientVersion (already read)
        // Next: UTF string for category
        ushort strLen = r.U16();
        var catStr = Encoding.UTF8.GetString(r.ReadBytes(strLen));
        var category = ParseCategory(catStr);

        var thing = new DatThingType { Category = category };

        // Read version-specific properties (DAT flags)
        if (clientVersion <= 854)
            ReadPropertiesV4(thing, r);
        else
            ReadProperties(thing, r);

        // Single frame group
        var fg = ReadFrameGroup(r);

        // V1: no animation data stored (OB reconstructs defaults)
        // Sprites: spriteId(U32) + dataSize(U32) + pixels(N bytes)
        var sprites = new Dictionary<uint, byte[]>();
        int total = fg.SpriteCount;
        fg.SpriteIndex = new uint[total];
        for (int i = 0; i < total; i++)
        {
            uint sprId = r.U32();
            uint dataSize = r.U32();
            var pixels = r.ReadBytes((int)dataSize);
            fg.SpriteIndex[i] = sprId;
            if (sprId != 0 && !sprites.ContainsKey(sprId))
                sprites[sprId] = PadOrTrimToArgb(pixels);
        }

        thing.FrameGroups = [fg];
        return new ObdData
        {
            ObdVersion = OBD_V1,
            ClientVersion = clientVersion,
            Category = category,
            Thing = thing,
            Sprites = sprites,
        };
    }

    private static ObdData DecodeV2(DatReader r)
    {
        // Version already read (200)
        ushort clientVersion = r.U16();
        byte catByte = r.U8();
        var category = (ThingCategory)catByte;
        r.U32(); // skip texture patterns position

        var thing = new DatThingType { Category = category };
        ReadProperties(thing, r);

        var fg = ReadFrameGroup(r);

        // Animation
        if (fg.Frames > 1)
        {
            fg.AnimationMode = (AnimationMode)r.U8();
            fg.LoopCount = r.S32();
            fg.StartFrame = r.S8();
            fg.FrameDurations = new FrameDuration[fg.Frames];
            for (int i = 0; i < fg.Frames; i++)
                fg.FrameDurations[i] = new FrameDuration { Minimum = r.U32(), Maximum = r.U32() };
        }

        // Sprites: spriteId(U32) + fixed 4096 bytes pixels
        var sprites = new Dictionary<uint, byte[]>();
        int total = fg.SpriteCount;
        fg.SpriteIndex = new uint[total];
        for (int i = 0; i < total; i++)
        {
            uint sprId = r.U32();
            var pixels = r.ReadBytes(SPRITE_DATA_SIZE);
            fg.SpriteIndex[i] = sprId;
            if (sprId != 0 && !sprites.ContainsKey(sprId))
                sprites[sprId] = pixels;
        }

        thing.FrameGroups = [fg];
        return new ObdData
        {
            ObdVersion = OBD_V2,
            ClientVersion = clientVersion,
            Category = category,
            Thing = thing,
            Sprites = sprites,
        };
    }

    private static ObdData DecodeV3(DatReader r)
    {
        // Version already read (300)
        ushort clientVersion = r.U16();
        byte catByte = r.U8();
        var category = (ThingCategory)catByte;
        r.U32(); // skip texture patterns position

        var thing = new DatThingType { Category = category };
        ReadProperties(thing, r);

        int groupCount = 1;
        if (category == ThingCategory.Outfit)
            groupCount = r.U8();

        var allSprites = new Dictionary<uint, byte[]>();
        var groups = new FrameGroup[groupCount];

        for (int g = 0; g < groupCount; g++)
        {
            if (category == ThingCategory.Outfit)
                r.U8(); // group type byte

            var fg = ReadFrameGroup(r);

            // Animation
            if (fg.Frames > 1)
            {
                fg.AnimationMode = (AnimationMode)r.U8();
                fg.LoopCount = r.S32();
                fg.StartFrame = r.S8();
                fg.FrameDurations = new FrameDuration[fg.Frames];
                for (int i = 0; i < fg.Frames; i++)
                    fg.FrameDurations[i] = new FrameDuration { Minimum = r.U32(), Maximum = r.U32() };
            }

            // Sprites: spriteId(U32) + dataSize(U32) + pixels(N bytes)
            int total = fg.SpriteCount;
            fg.SpriteIndex = new uint[total];
            for (int i = 0; i < total; i++)
            {
                uint sprId = r.U32();
                uint dataSize = r.U32();
                var pixels = r.ReadBytes((int)dataSize);
                fg.SpriteIndex[i] = sprId;
                if (sprId != 0 && !allSprites.ContainsKey(sprId))
                    allSprites[sprId] = PadOrTrimToArgb(pixels);
            }

            fg.Type = (FrameGroupType)g;
            groups[g] = fg;
        }

        thing.FrameGroups = groups;
        return new ObdData
        {
            ObdVersion = OBD_V3,
            ClientVersion = clientVersion,
            Category = category,
            Thing = thing,
            Sprites = allSprites,
        };
    }

    // ═══════════════════════════════════════════════════════════════
    //  ENCODE
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Encode a thing + its sprites into OBD V2 format (most compatible with OB).
    /// <paramref name="spriteRgbaProvider"/> should return 4096-byte RGBA for a given sprite ID.
    /// </summary>
    public static byte[] Encode(DatThingType thing, ushort clientVersion,
        Func<uint, byte[]?> spriteRgbaProvider, ushort obdVersion = OBD_V2)
    {
        return obdVersion switch
        {
            OBD_V3 => EncodeV3(thing, clientVersion, spriteRgbaProvider),
            OBD_V2 => EncodeV2(thing, clientVersion, spriteRgbaProvider),
            _ => EncodeV2(thing, clientVersion, spriteRgbaProvider),
        };
    }

    private static byte[] EncodeV2(DatThingType thing, ushort clientVersion,
        Func<uint, byte[]?> spriteRgbaProvider)
    {
        var w = new DatWriter();

        w.U16(OBD_V2);           // OBD version
        w.U16(clientVersion);    // Client version
        w.U8((byte)thing.Category); // Category

        // Reserve 4 bytes for texture patterns position
        int posOffset = w.Position;
        w.U32(0);

        WriteProperties(w, thing);

        // Write texture patterns position
        int pos = w.Position;
        w.WriteAt(posOffset, pos);

        var fg = thing.FrameGroups.Length > 0 ? thing.FrameGroups[0] : new FrameGroup();

        w.U8(fg.Width);
        w.U8(fg.Height);
        if (fg.Width > 1 || fg.Height > 1)
            w.U8(fg.ExactSize);

        w.U8(fg.Layers);
        w.U8(fg.PatternX);
        w.U8(fg.PatternY);
        w.U8(fg.PatternZ > 0 ? fg.PatternZ : (byte)1);
        w.U8(fg.Frames);

        // Animation
        if (fg.Frames > 1)
        {
            w.U8((byte)fg.AnimationMode);
            w.S32(fg.LoopCount);
            w.S8(fg.StartFrame);
            for (int i = 0; i < fg.Frames; i++)
            {
                var dur = i < fg.FrameDurations.Length ? fg.FrameDurations[i] : new FrameDuration { Minimum = 100, Maximum = 100 };
                w.U32(dur.Minimum);
                w.U32(dur.Maximum);
            }
        }

        // Sprites: spriteId(U32) + fixed 4096 bytes
        for (int i = 0; i < fg.SpriteIndex.Length; i++)
        {
            uint sprId = fg.SpriteIndex[i];
            w.U32(sprId);
            var rgba = spriteRgbaProvider(sprId);
            if (rgba != null && rgba.Length >= SPRITE_DATA_SIZE)
                w.Bytes(rgba.AsSpan(0, SPRITE_DATA_SIZE));
            else
            {
                // Pad with transparent black
                var empty = new byte[SPRITE_DATA_SIZE];
                if (rgba != null)
                    Buffer.BlockCopy(rgba, 0, empty, 0, Math.Min(rgba.Length, SPRITE_DATA_SIZE));
                w.Bytes(empty);
            }
        }

        return LzmaCompress(w.ToArray());
    }

    private static byte[] EncodeV3(DatThingType thing, ushort clientVersion,
        Func<uint, byte[]?> spriteRgbaProvider)
    {
        var w = new DatWriter();

        w.U16(OBD_V3);
        w.U16(clientVersion);
        w.U8((byte)thing.Category);

        int posOffset = w.Position;
        w.U32(0); // reserve texture patterns position

        WriteProperties(w, thing);

        int pos = w.Position;
        w.WriteAt(posOffset, pos);

        int groupCount = thing.FrameGroups.Length;
        if (thing.Category == ThingCategory.Outfit)
            w.U8((byte)groupCount);

        for (int g = 0; g < groupCount; g++)
        {
            if (thing.Category == ThingCategory.Outfit)
            {
                byte groupId = (byte)(groupCount < 2 ? 1 : g);
                w.U8(groupId);
            }

            var fg = thing.FrameGroups[g];
            w.U8(fg.Width);
            w.U8(fg.Height);
            if (fg.Width > 1 || fg.Height > 1)
                w.U8(fg.ExactSize);

            w.U8(fg.Layers);
            w.U8(fg.PatternX);
            w.U8(fg.PatternY);
            w.U8(fg.PatternZ > 0 ? fg.PatternZ : (byte)1);
            w.U8(fg.Frames);

            if (fg.Frames > 1)
            {
                w.U8((byte)fg.AnimationMode);
                w.S32(fg.LoopCount);
                w.S8(fg.StartFrame);
                for (int i = 0; i < fg.Frames; i++)
                {
                    var dur = i < fg.FrameDurations.Length ? fg.FrameDurations[i] : new FrameDuration { Minimum = 100, Maximum = 100 };
                    w.U32(dur.Minimum);
                    w.U32(dur.Maximum);
                }
            }

            // Sprites: spriteId(U32) + dataSize(U32) + pixels
            for (int i = 0; i < fg.SpriteIndex.Length; i++)
            {
                uint sprId = fg.SpriteIndex[i];
                var rgba = spriteRgbaProvider(sprId);
                w.U32(sprId);
                if (rgba != null && rgba.Length > 0)
                {
                    w.U32((uint)rgba.Length);
                    w.Bytes(rgba);
                }
                else
                {
                    var empty = new byte[SPRITE_DATA_SIZE];
                    w.U32((uint)empty.Length);
                    w.Bytes(empty);
                }
            }
        }

        return LzmaCompress(w.ToArray());
    }

    // ═══════════════════════════════════════════════════════════════
    //  PROPERTIES (read/write)
    //  Uses the same flag IDs as Object Builder's OBDEncoder
    // ═══════════════════════════════════════════════════════════════

    private static void ReadProperties(DatThingType thing, DatReader r)
    {
        while (true)
        {
            byte flag = r.U8();
            if (flag == LAST_FLAG) break;

            switch (flag)
            {
                case GROUND: thing.IsGround = true; thing.GroundSpeed = r.U16(); break;
                case GROUND_BORDER: thing.IsGroundBorder = true; break;
                case ON_BOTTOM: thing.IsOnBottom = true; break;
                case ON_TOP: thing.IsOnTop = true; break;
                case CONTAINER: thing.IsContainer = true; break;
                case STACKABLE: thing.IsStackable = true; break;
                case FORCE_USE: thing.IsForceUse = true; break;
                case MULTI_USE: thing.IsMultiUse = true; break;
                case WRITABLE: thing.IsWritable = true; thing.MaxTextLength = r.U16(); break;
                case WRITABLE_ONCE: thing.IsWritableOnce = true; thing.MaxTextLength = r.U16(); break;
                case FLUID_CONTAINER: thing.IsFluidContainer = true; break;
                case FLUID: thing.IsFluid = true; break;
                case UNPASSABLE: thing.IsUnpassable = true; break;
                case UNMOVEABLE: thing.IsUnmoveable = true; break;
                case BLOCK_MISSILE: thing.IsBlockMissile = true; break;
                case BLOCK_PATHFIND: thing.IsBlockPathfind = true; break;
                case NO_MOVE_ANIMATION: thing.IsNoMoveAnimation = true; break;
                case PICKUPABLE: thing.IsPickupable = true; break;
                case HANGABLE: thing.IsHangable = true; break;
                case HOOK_SOUTH: thing.IsVertical = true; break;
                case HOOK_EAST: thing.IsHorizontal = true; break;
                case ROTATABLE: thing.IsRotatable = true; break;
                case HAS_LIGHT: thing.HasLight = true; thing.LightLevel = r.U16(); thing.LightColor = r.U16(); break;
                case DONT_HIDE: thing.IsDontHide = true; break;
                case TRANSLUCENT: thing.IsTranslucent = true; break;
                case HAS_OFFSET: thing.HasOffset = true; thing.OffsetX = r.S16(); thing.OffsetY = r.S16(); break;
                case HAS_ELEVATION: thing.HasElevation = true; thing.Elevation = r.U16(); break;
                case LYING_OBJECT: thing.IsLyingObject = true; break;
                case ANIMATE_ALWAYS: thing.IsAnimateAlways = true; break;
                case MINI_MAP: thing.IsMiniMap = true; thing.MiniMapColor = r.U16(); break;
                case LENS_HELP: thing.IsLensHelp = true; thing.LensHelp = r.U16(); break;
                case FULL_GROUND: thing.IsFullGround = true; break;
                case IGNORE_LOOK: thing.IsIgnoreLook = true; break;
                case CLOTH: thing.IsCloth = true; thing.ClothSlot = r.U16(); break;
                case MARKET_ITEM:
                    thing.IsMarketItem = true;
                    thing.MarketCategory = r.U16();
                    thing.MarketTradeAs = r.U16();
                    thing.MarketShowAs = r.U16();
                    ushort nameLen = r.U16();
                    thing.MarketName = Encoding.Latin1.GetString(r.ReadBytes(nameLen));
                    thing.MarketRestrictProfession = r.U16();
                    thing.MarketRestrictLevel = r.U16();
                    break;
                case DEFAULT_ACTION: thing.HasDefaultAction = true; thing.DefaultAction = r.U16(); break;
                case WRAPPABLE: thing.IsWrappable = true; break;
                case UNWRAPPABLE: thing.IsUnwrappable = true; break;
                case TOP_EFFECT: thing.IsTopEffect = true; break;
                case HAS_CHARGES: break; // boolean only
                case FLOOR_CHANGE: break; // boolean only
                case USABLE: thing.IsUsable = true; break;
                default:
                    throw new InvalidDataException($"Unknown OBD flag 0x{flag:X2}");
            }
        }
    }

    /// <summary>Simplified V4 property reader for OBD V1 with client ≤ 854.</summary>
    private static void ReadPropertiesV4(DatThingType thing, DatReader r)
    {
        // V4 properties use a different flag layout than the OBD-standard one.
        // For simplicity, re-use the standard reader — OBD V1 files from OB
        // actually use ThingSerializer which may differ. The standard reader
        // handles all known OBD flag IDs.
        ReadProperties(thing, r);
    }

    private static void WriteProperties(DatWriter w, DatThingType t)
    {
        if (t.IsGround) { w.U8(GROUND); w.U16(t.GroundSpeed); }
        if (t.IsGroundBorder) w.U8(GROUND_BORDER);
        if (t.IsOnBottom) w.U8(ON_BOTTOM);
        if (t.IsOnTop) w.U8(ON_TOP);
        if (t.IsContainer) w.U8(CONTAINER);
        if (t.IsStackable) w.U8(STACKABLE);
        if (t.IsForceUse) w.U8(FORCE_USE);
        if (t.IsMultiUse) w.U8(MULTI_USE);
        if (t.IsWritable) { w.U8(WRITABLE); w.U16(t.MaxTextLength); }
        if (t.IsWritableOnce) { w.U8(WRITABLE_ONCE); w.U16(t.MaxTextLength); }
        if (t.IsFluidContainer) w.U8(FLUID_CONTAINER);
        if (t.IsFluid) w.U8(FLUID);
        if (t.IsUnpassable) w.U8(UNPASSABLE);
        if (t.IsUnmoveable) w.U8(UNMOVEABLE);
        if (t.IsBlockMissile) w.U8(BLOCK_MISSILE);
        if (t.IsBlockPathfind) w.U8(BLOCK_PATHFIND);
        if (t.IsNoMoveAnimation) w.U8(NO_MOVE_ANIMATION);
        if (t.IsPickupable) w.U8(PICKUPABLE);
        if (t.IsHangable) w.U8(HANGABLE);
        if (t.IsVertical) w.U8(HOOK_SOUTH);
        if (t.IsHorizontal) w.U8(HOOK_EAST);
        if (t.IsRotatable) w.U8(ROTATABLE);
        if (t.HasLight) { w.U8(HAS_LIGHT); w.U16(t.LightLevel); w.U16(t.LightColor); }
        if (t.IsDontHide) w.U8(DONT_HIDE);
        if (t.IsTranslucent) w.U8(TRANSLUCENT);
        if (t.HasOffset) { w.U8(HAS_OFFSET); w.S16(t.OffsetX); w.S16(t.OffsetY); }
        if (t.HasElevation) { w.U8(HAS_ELEVATION); w.U16(t.Elevation); }
        if (t.IsLyingObject) w.U8(LYING_OBJECT);
        if (t.IsAnimateAlways) w.U8(ANIMATE_ALWAYS);
        if (t.IsMiniMap) { w.U8(MINI_MAP); w.U16(t.MiniMapColor); }
        if (t.IsLensHelp) { w.U8(LENS_HELP); w.U16(t.LensHelp); }
        if (t.IsFullGround) w.U8(FULL_GROUND);
        if (t.IsIgnoreLook) w.U8(IGNORE_LOOK);
        if (t.IsCloth) { w.U8(CLOTH); w.U16(t.ClothSlot); }
        if (t.IsMarketItem)
        {
            w.U8(MARKET_ITEM);
            w.U16(t.MarketCategory);
            w.U16(t.MarketTradeAs);
            w.U16(t.MarketShowAs);
            var nameBytes = Encoding.Latin1.GetBytes(t.MarketName ?? string.Empty);
            w.U16((ushort)nameBytes.Length);
            w.Bytes(nameBytes);
            w.U16(t.MarketRestrictProfession);
            w.U16(t.MarketRestrictLevel);
        }
        if (t.HasDefaultAction) { w.U8(DEFAULT_ACTION); w.U16(t.DefaultAction); }
        if (t.IsWrappable) w.U8(WRAPPABLE);
        if (t.IsUnwrappable) w.U8(UNWRAPPABLE);
        if (t.IsTopEffect) w.U8(TOP_EFFECT);
        if (t.IsUsable) w.U8(USABLE);
        w.U8(LAST_FLAG);
    }

    // ═══════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════

    private static FrameGroup ReadFrameGroup(DatReader r)
    {
        var fg = new FrameGroup();
        fg.Width = r.U8();
        fg.Height = r.U8();
        if (fg.Width > 1 || fg.Height > 1)
            fg.ExactSize = r.U8();
        else
            fg.ExactSize = 32;

        fg.Layers = r.U8();
        fg.PatternX = r.U8();
        fg.PatternY = r.U8();
        fg.PatternZ = r.U8();
        fg.Frames = r.U8();
        return fg;
    }

    private static ThingCategory ParseCategory(string s)
    {
        return s.ToLowerInvariant() switch
        {
            "item" => ThingCategory.Item,
            "outfit" => ThingCategory.Outfit,
            "effect" => ThingCategory.Effect,
            "missile" => ThingCategory.Missile,
            _ => ThingCategory.Item,
        };
    }

    /// <summary>Ensure pixel data is exactly 4096 bytes (32×32 RGBA).</summary>
    private static byte[] PadOrTrimToArgb(byte[] data)
    {
        if (data.Length == SPRITE_DATA_SIZE) return data;
        var result = new byte[SPRITE_DATA_SIZE];
        Buffer.BlockCopy(data, 0, result, 0, Math.Min(data.Length, SPRITE_DATA_SIZE));
        return result;
    }

    // ═══════════════════════════════════════════════════════════════
    //  LZMA COMPRESSION (compatible with Flash's ByteArray LZMA)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Decompress LZMA-alone format: 5-byte props + 8-byte size + compressed data.
    /// </summary>
    private static byte[] LzmaDecompress(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);

        // Read LZMA properties (5 bytes)
        var props = new byte[5];
        if (input.Read(props, 0, 5) != 5)
            throw new InvalidDataException("LZMA: cannot read properties");

        // Read uncompressed size (8 bytes LE)
        var sizeBytes = new byte[8];
        if (input.Read(sizeBytes, 0, 8) != 8)
            throw new InvalidDataException("LZMA: cannot read uncompressed size");
        long uncompressedSize = BitConverter.ToInt64(sizeBytes, 0);

        using var output = new MemoryStream();
        using var lzma = new LzmaStream(props, input, compressed.Length - 13, uncompressedSize);
        lzma.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>
    /// Compress to LZMA-alone format: 5-byte props + 8-byte size + compressed data.
    /// LzmaStream in compress mode (isPackedEncoder=false) writes: props(5) + compressed data.
    /// We split at byte 5 and insert the 8-byte uncompressed size for Flash/OB compatibility.
    /// </summary>
    private static byte[] LzmaCompress(byte[] raw)
    {
        using var tempOutput = new MemoryStream();
        var encoderProps = new LzmaEncoderProperties(false, 1 << 20, 64);

        using (var lzma = new LzmaStream(encoderProps, false, tempOutput))
        {
            lzma.Write(raw, 0, raw.Length);
        }

        var allBytes = tempOutput.ToArray();
        // allBytes = [props(5)] [compressed data]

        using var final = new MemoryStream();
        final.Write(allBytes, 0, 5); // LZMA properties
        final.Write(BitConverter.GetBytes((long)raw.Length), 0, 8); // uncompressed size
        final.Write(allBytes, 5, allBytes.Length - 5); // compressed data
        return final.ToArray();
    }

    // ── Reuse DatFile's DatWriter/DatReader with Position + WriteAt ──

    private sealed class DatWriter
    {
        private readonly MemoryStream _ms = new();

        public int Position => (int)_ms.Position;

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

        public void Bytes(ReadOnlySpan<byte> data) => _ms.Write(data);

        public void WriteAt(int offset, int value)
        {
            var pos = _ms.Position;
            _ms.Position = offset;
            Span<byte> buf = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buf, value);
            _ms.Write(buf);
            _ms.Position = pos;
        }

        public byte[] ToArray() => _ms.ToArray();
    }

    // Reuse DatReader from DatFile — but we need our own since it's internal
    private sealed class DatReader(byte[] data)
    {
        private int _pos;

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

        public byte[] ReadBytes(int count)
        {
            var result = new byte[count];
            Buffer.BlockCopy(data, _pos, result, 0, count);
            _pos += count;
            return result;
        }
    }
}
