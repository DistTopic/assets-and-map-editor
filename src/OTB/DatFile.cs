using System.Buffers.Binary;

namespace POriginsItemEditor.OTB;

/// <summary>
/// Minimal Tibia.dat parser for protocol 1098.
/// Extracts animation phase count per client-ID so we can cross-reference with OTB.
/// </summary>
public static class DatFile
{
    /// <summary>
    /// Load a .dat file and return a dictionary mapping client-ID → animation phase count.
    /// Only items are parsed (outfits/effects/missiles are skipped).
    /// </summary>
    public static DatData Load(string path)
    {
        var raw = File.ReadAllBytes(path);
        return Parse(raw);
    }

    private static DatData Parse(byte[] raw)
    {
        var r = new DatReader(raw);

        var signature = r.U32();
        var numItems = r.U16();
        var numOutfits = r.U16();
        var numEffects = r.U16();
        var numMissiles = r.U16();

        const int firstId = 100;
        var lastId = firstId + numItems - 1;

        var items = new Dictionary<ushort, DatItemInfo>(numItems);

        for (int id = firstId; id <= lastId; id++)
        {
            var info = ParseThing(r, isCreature: false);
            items[(ushort)id] = info;
        }

        return new DatData
        {
            Signature = signature,
            ItemCount = numItems,
            OutfitCount = numOutfits,
            EffectCount = numEffects,
            MissileCount = numMissiles,
            Items = items,
        };
    }

    private static DatItemInfo ParseThing(DatReader r, bool isCreature)
    {
        var attrs = ParseAttrs(r);

        uint firstSpriteId = 0;
        int groupCount = 1;
        if (isCreature)
            groupCount = r.U8();

        int animPhases = 0;
        for (int g = 0; g < groupCount; g++)
        {
            if (isCreature) r.U8(); // frameGroupType

            int w = r.U8();
            int h = r.U8();
            if (w > 1 || h > 1) r.U8(); // realSize

            int layers = r.U8();
            int patX = r.U8();
            int patY = r.U8();
            int patZ = r.U8();

            int phases = r.U8();
            animPhases += phases;

            if (phases > 1) // Enhanced animations (active for 1098)
            {
                r.U8();  // async
                r.S32(); // loopCount
                r.S8();  // startPhase
                for (int i = 0; i < phases; i++)
                {
                    r.U32(); // min
                    r.U32(); // max
                }
            }

            int totalSprites = w * h * layers * patX * patY * patZ * phases;
            for (int i = 0; i < totalSprites; i++)
            {
                uint sid = r.U32();
                if (i == 0 && g == 0) firstSpriteId = sid;
            }
        }

        return new DatItemInfo
        {
            AnimPhases = animPhases,
            AnimateAlways = attrs.Contains(27), // ThingAttrAnimateAlways
            FirstSpriteId = firstSpriteId,
        };
    }

    private static HashSet<int> ParseAttrs(DatReader r)
    {
        var attrs = new HashSet<int>();
        for (int safety = 0; safety < 255; safety++)
        {
            int raw = r.U8();
            if (raw == 0xFF) break;

            int attr = RemapAttr(raw);
            attrs.Add(attr);

            // Attrs with payloads
            if (attr is 0 or 25 or 8 or 9 or 28 or 32 or 29 or 251) // U16 payload
                r.U16();
            else if (attr is 24 or 21) // 2x U16 payload
            {
                r.U16();
                r.U16();
            }
            else if (attr == 33) // Market
            {
                r.U16(); // category
                r.U16(); // tradeAs
                r.U16(); // showAs
                var nameLen = r.U16();
                r.Skip(nameLen);
                r.U16(); // voc
                r.U16(); // level
            }
            // else: boolean flag, no payload
        }
        return attrs;
    }

    /// <summary>Version >=1000 attribute remapping.</summary>
    private static int RemapAttr(int raw)
    {
        if (raw == 16) return 253;  // NoMoveAnimation
        if (raw == 254) return 34;  // Usable
        if (raw == 35) return 251;  // DefaultAction
        if (raw > 16) return raw - 1;
        return raw;
    }

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

    /// <summary>Client-ID → item animation info.</summary>
    public Dictionary<ushort, DatItemInfo> Items { get; init; } = [];
}

public sealed class DatItemInfo
{
    public int AnimPhases { get; init; }
    public bool AnimateAlways { get; init; }
    public uint FirstSpriteId { get; init; }
}
