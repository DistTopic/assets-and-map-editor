using System.Buffers.Binary;
using System.Text;

namespace AssetsAndMapEditor.OTB;

/// <summary>
/// Tibia.dat parser supporting multiple protocol versions.
/// Supports MetadataFlags6 (1100, 1098+), and legacy protocols (854, 860).
/// </summary>
public static class DatFile
{
    /// <summary>Optional log callback for diagnostic messages during load.</summary>
    public static Action<string>? DiagLog { get; set; }

    /// <summary>
    /// Load a DAT file. Feature flags (extended, improvedAnimations, frameGroups)
    /// are independent from the protocol version, matching Object Builder's architecture.
    /// When null, each feature defaults based on the protocol (OB: || version >= threshold).
    /// </summary>
    public static DatData Load(string path, int protocolHint = 0,
        bool? extended = null, bool? improvedAnimations = null, bool? frameGroups = null)
    {
        var raw = File.ReadAllBytes(path);
        var sig = BinaryPrimitives.ReadUInt32LittleEndian(raw);
        int detected = DetectProtocol(sig);

        DiagLog?.Invoke($"[DAT] File={Path.GetFileName(path)}, size={raw.Length}, sig=0x{sig:X8}, detectedProto={detected}, hint={protocolHint}");

        int primary = protocolHint > 0 ? protocolHint : detected;

        // If caller provides explicit feature flags, use them directly (OB approach)
        if (extended.HasValue)
        {
            bool ext = extended.Value || primary >= 960;
            bool anim = (improvedAnimations ?? false) || primary >= 1050;
            bool fg = (frameGroups ?? false) || primary >= 1057;
            try
            {
                var result = Parse(raw, primary, ext, anim, fg);
                DiagLog?.Invoke($"[DAT] Parse OK: proto={primary}, ext={ext}, anim={anim}, fg={fg}, items={result.ItemCount}");
                return result;
            }
            catch (Exception ex)
            {
                DiagLog?.Invoke($"[DAT] Parse FAILED (explicit flags): proto={primary}, ext={ext}, anim={anim}, fg={fg}: {ex.Message}");
                throw new InvalidOperationException(
                    $"Failed to parse {Path.GetFileName(path)} with explicit flags (proto={primary}, ext={ext}, anim={anim}, fg={fg}): {ex.Message}", ex);
            }
        }

        // Auto-detect: try feature combinations for the primary protocol.
        // OB order: all features on first, then protocol defaults, then off.
        var featureCombos = new (bool ext, bool anim, bool fg)[]
        {
            (true, true, true),                                              // all on
            (primary >= 960, primary >= 1050, primary >= 1057),             // protocol defaults
            (true, primary >= 1050, primary >= 1057),                       // ext override
            (primary >= 960, false, false),                                  // no anim/fg
            (false, false, false),                                           // all off
        };

        foreach (var (ext, anim, fg) in featureCombos)
        {
            try
            {
                var result = Parse(raw, primary, ext, anim, fg);
                DiagLog?.Invoke($"[DAT] Parse OK: proto={primary}, ext={ext}, anim={anim}, fg={fg}, items={result.ItemCount}");
                return result;
            }
            catch (Exception ex)
            {
                DiagLog?.Invoke($"[DAT] Parse FAILED: proto={primary}, ext={ext}, anim={anim}, fg={fg}: {ex.Message}");
            }
        }

        // Fallback: try other protocols with protocol-default features
        int[] allProtocols = [1100, 1098, 1076, 1057, 1050, 960, 860, 854, 810, 800, 790, 780, 770, 760, 750, 740];
        foreach (var proto in allProtocols)
        {
            if (proto == primary) continue;
            bool extD = proto >= 960, animD = proto >= 1050, fgD = proto >= 1057;
            try
            {
                var result = Parse(raw, proto, extD, animD, fgD);
                DiagLog?.Invoke($"[DAT] Fallback OK: proto={proto}, ext={extD}, anim={animD}, fg={fgD}, items={result.ItemCount}");
                return result;
            }
            catch { }
        }

        throw new InvalidOperationException(
            $"Failed to parse {Path.GetFileName(path)} (sig=0x{sig:X8}, size={raw.Length}). No protocol/feature combination worked.");
    }

    /// <summary>
    /// Detects the protocol version from the DAT signature.
    /// Known signatures map to specific versions; unknown defaults to 1098.
    /// </summary>
    public static int DetectProtocol(uint signature)
    {
        // Well-known Tibia.dat signatures (from Object Builder / OTClient sources)
        // Pre-10.71: full 4-byte signatures (e.g. 0x4A10DC35)
        // 10.71+: only lower 2 bytes are significant (e.g. 0x0000334F)
        return signature switch
        {
            // Legacy 4-byte signatures (7.40 – 10.70)
            0x439D5A33 => 740,
            0x41BF05F4 => 750,
            0x41BF05F5 => 760,
            0x4708AEF5 => 770,
            0x4721F8A2 => 780,
            0x4782ADE5 => 790,
            0x47A11B85 => 800,
            0x4865975E => 810,
            0x49971E5E => 854,
            0x4B1E2CAA => 854,  // PStory / custom Pokemon DAT (854 protocol)
            0x4A10DC35 => 860,
            0x4C4B6B22 => 960,
            0x4D455ADE => 1050,
            0x4E97D9D4 => 1057,
            0x500F744E => 1076,
            0x50C5A941 => 1098,
            0x51D6A2D3 => 1100,

            // Short 2-byte signatures (10.71+ / format 10.57)
            // From 10.71 onwards the upper 2 bytes of the DAT signature are zero.
            0x334F => 1071,
            0x3729 => 1072,
            0x374D => 1073,
            0x375E => 1074,
            0x3775 => 1075,
            0x37DF => 1076,
            0x38DE => 1077,
            0x3F26 => 1090,
            0x3F81 => 1091,
            0x4086 => 1092,
            0x40FF => 1093,  // 10.93 test
            0x413F => 1093,
            0x41E5 => 1094,
            0x41F3 => 1095,
            0x42A3 => 1098,
            0x4347 => 1099,
            0x4A10 => 1100,  // 12.71+ clients (format 10.57, protocol 1100)

            _ => 1100,
        };
    }

    private static DatData Parse(byte[] raw, int protocolHint, bool extended, bool improvedAnimations, bool frameGroups)
    {
        var r = new DatReader(raw);

        var signature = r.U32();
        int protocol = protocolHint > 0 ? protocolHint : DetectProtocol(signature);

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
            try
            {
                var thing = ParseThing(r, (ushort)id, ThingCategory.Item, protocol, extended, improvedAnimations, frameGroups);
                items[(ushort)id] = thing;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed parsing Item {id}/{lastItemId} at reader offset {r.Position} (protocol {protocol}): {ex.Message}", ex);
            }
        }

        // Parse outfits/effects/missiles independently — don't let failures block items
        bool secondaryFailed = false;
        try
        {
            for (int id = 1; id <= numOutfits; id++)
                outfits[(ushort)id] = ParseThing(r, (ushort)id, ThingCategory.Outfit, protocol, extended, improvedAnimations, frameGroups);

            for (int id = 1; id <= numEffects; id++)
                effects[(ushort)id] = ParseThing(r, (ushort)id, ThingCategory.Effect, protocol, extended, improvedAnimations, frameGroups);

            for (int id = 1; id <= numMissiles; id++)
                missiles[(ushort)id] = ParseThing(r, (ushort)id, ThingCategory.Missile, protocol, extended, improvedAnimations, frameGroups);
        }
        catch
        {
            secondaryFailed = true;
        }

        // If outfit/effect/missile parsing failed AND most of the file is unread,
        // the protocol is almost certainly wrong — reject so fallback tries the next one.
        // But if items parsed OK and we consumed most of the file, keep the result.
        if (secondaryFailed)
        {
            int remaining = raw.Length - r.Position;
            double parsedRatio = (double)r.Position / raw.Length;
            int expectedSecondary = (numOutfits + numEffects + numMissiles) * 10; // rough minimum bytes

            // Only reject if we have significant unparsed data AND haven't consumed most of the file
            if (remaining > expectedSecondary / 2 && remaining > 1000 && parsedRatio < 0.5)
                throw new InvalidOperationException(
                    $"Secondary categories failed with {remaining} bytes remaining ({parsedRatio:P0} parsed, protocol {protocol}, ext={extended}, anim={improvedAnimations}, fg={frameGroups}) — likely wrong protocol.");
        }

        return new DatData
        {
            Signature = signature,
            ProtocolVersion = protocol,
            Extended = extended,
            ImprovedAnimations = improvedAnimations,
            FrameGroups = frameGroups,
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

    private static DatThingType ParseThing(DatReader r, ushort id, ThingCategory category,
        int protocol, bool extended, bool improvedAnimations, bool hasFrameGroups)
    {
        var thing = new DatThingType { Id = id, Category = category };

        // ── Parse flags ── (determined by protocol only — flag byte format)
        ParseFlags(r, thing, protocol);

        // ── Parse frame groups ── (OB: frameGroups && category == OUTFIT)
        bool isOutfit = category == ThingCategory.Outfit;
        bool useFrameGroups = hasFrameGroups && isOutfit;
        int groupCount = useFrameGroups ? r.U8() : 1;
        var groups = new FrameGroup[groupCount];

        for (int g = 0; g < groupCount; g++)
        {
            var fg = new FrameGroup();
            if (useFrameGroups)
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

            // Improved animations (OB: frameDurations flag, independent of protocol)
            if (fg.Frames > 1 && improvedAnimations)
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

            // Sprite index size: U32 if extended, U16 otherwise
            int totalSprites = fg.SpriteCount;
            fg.SpriteIndex = new uint[totalSprites];
            for (int i = 0; i < totalSprites; i++)
                fg.SpriteIndex[i] = extended ? r.U32() : r.U16();

            groups[g] = fg;
        }

        thing.FrameGroups = groups;
        return thing;
    }

    private static void ParseFlags(DatReader r, DatThingType thing, int protocol)
    {
        while (true)
        {
            int flag = r.U8();
            if (flag == 0xFF) break;

            // Dispatch to the correct flag set based on protocol version.
            // MetadataFlags3: client 7.55-7.72  (protocol <= 772)
            // MetadataFlags4: client 7.80-8.54  (protocol <= 854)
            // MetadataFlags5: client 8.60-9.86  (protocol <= 986)
            // MetadataFlags6: client 10.10+     (protocol > 986)
            if (protocol <= 772)
                ParseFlagV3(r, thing, flag);
            else if (protocol <= 854)
                ParseFlagV4(r, thing, flag);
            else if (protocol <= 986)
                ParseFlagV5(r, thing, flag);
            else
                ParseFlagV6(r, thing, flag);
        }
    }

    /// <summary>MetadataFlags6 — client 10.10+ (protocol &gt;= 1010).</summary>
    private static void ParseFlagV6(DatReader r, DatThingType thing, int flag)
    {
        switch (flag)
        {
            case 0x00: thing.IsGround = true; thing.GroundSpeed = r.U16(); break;
            case 0x01: thing.IsGroundBorder = true; break;
            case 0x02: thing.IsOnBottom = true; break;
            case 0x03: thing.IsOnTop = true; break;
            case 0x04: thing.IsContainer = true; break;
            case 0x05: thing.IsStackable = true; break;
            case 0x06: thing.IsForceUse = true; break;
            case 0x07: thing.IsMultiUse = true; break;
            case 0x08: thing.IsWritable = true; thing.MaxTextLength = r.U16(); break;
            case 0x09: thing.IsWritableOnce = true; thing.MaxTextLength = r.U16(); break;
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
            case 0x16: thing.HasLight = true; thing.LightLevel = r.U16(); thing.LightColor = r.U16(); break;
            case 0x17: thing.IsDontHide = true; break;
            case 0x18: thing.IsTranslucent = true; break;
            case 0x19: thing.HasOffset = true; thing.OffsetX = r.S16(); thing.OffsetY = r.S16(); break;
            case 0x1A: thing.HasElevation = true; thing.Elevation = r.U16(); break;
            case 0x1B: thing.IsLyingObject = true; break;
            case 0x1C: thing.IsAnimateAlways = true; break;
            case 0x1D: thing.IsMiniMap = true; thing.MiniMapColor = r.U16(); break;
            case 0x1E: thing.IsLensHelp = true; thing.LensHelp = r.U16(); break;
            case 0x1F: thing.IsFullGround = true; break;
            case 0x20: thing.IsIgnoreLook = true; break;
            case 0x21: thing.IsCloth = true; thing.ClothSlot = r.U16(); break;
            case 0x22:
                thing.IsMarketItem = true;
                thing.MarketCategory = r.U16();
                thing.MarketTradeAs = r.U16();
                thing.MarketShowAs = r.U16();
                var nameLen6 = r.U16();
                if (nameLen6 > 512) nameLen6 = 512;
                var rawName6 = r.String(nameLen6);
                thing.MarketName = new string(rawName6.Where(c => c >= 0x20 && c <= 0x7E).ToArray());
                thing.MarketRestrictProfession = r.U16();
                thing.MarketRestrictLevel = r.U16();
                break;
            case 0x23: thing.HasDefaultAction = true; thing.DefaultAction = r.U16(); break;
            case 0x24: thing.IsWrappable = true; break;
            case 0x25: thing.IsUnwrappable = true; break;
            case 0x26: thing.IsTopEffect = true; break;
            case 0xFE: thing.IsUsable = true; break;
        }
    }

    /// <summary>MetadataFlags5 — client 8.60-9.86 (protocol 860-1009). No NO_MOVE_ANIMATION; flags 0x10+ shifted by -1 vs V6.</summary>
    private static void ParseFlagV5(DatReader r, DatThingType thing, int flag)
    {
        switch (flag)
        {
            case 0x00: thing.IsGround = true; thing.GroundSpeed = r.U16(); break;
            case 0x01: thing.IsGroundBorder = true; break;
            case 0x02: thing.IsOnBottom = true; break;
            case 0x03: thing.IsOnTop = true; break;
            case 0x04: thing.IsContainer = true; break;
            case 0x05: thing.IsStackable = true; break;
            case 0x06: thing.IsForceUse = true; break;
            case 0x07: thing.IsMultiUse = true; break;
            case 0x08: thing.IsWritable = true; thing.MaxTextLength = r.U16(); break;
            case 0x09: thing.IsWritableOnce = true; thing.MaxTextLength = r.U16(); break;
            case 0x0A: thing.IsFluidContainer = true; break;
            case 0x0B: thing.IsFluid = true; break;
            case 0x0C: thing.IsUnpassable = true; break;
            case 0x0D: thing.IsUnmoveable = true; break;
            case 0x0E: thing.IsBlockMissile = true; break;
            case 0x0F: thing.IsBlockPathfind = true; break;
            // 0x10 = Pickupable (no NoMoveAnimation in MF5)
            case 0x10: thing.IsPickupable = true; break;
            case 0x11: thing.IsHangable = true; break;
            case 0x12: thing.IsVertical = true; break;
            case 0x13: thing.IsHorizontal = true; break;
            case 0x14: thing.IsRotatable = true; break;
            case 0x15: thing.HasLight = true; thing.LightLevel = r.U16(); thing.LightColor = r.U16(); break;
            case 0x16: thing.IsDontHide = true; break;
            case 0x17: thing.IsTranslucent = true; break;
            case 0x18: thing.HasOffset = true; thing.OffsetX = r.S16(); thing.OffsetY = r.S16(); break;
            case 0x19: thing.HasElevation = true; thing.Elevation = r.U16(); break;
            case 0x1A: thing.IsLyingObject = true; break;
            case 0x1B: thing.IsAnimateAlways = true; break;
            case 0x1C: thing.IsMiniMap = true; thing.MiniMapColor = r.U16(); break;
            case 0x1D: thing.IsLensHelp = true; thing.LensHelp = r.U16(); break;
            case 0x1E: thing.IsFullGround = true; break;
            case 0x1F: thing.IsIgnoreLook = true; break;
            case 0x20: thing.IsCloth = true; thing.ClothSlot = r.U16(); break;
            case 0x21:
                thing.IsMarketItem = true;
                thing.MarketCategory = r.U16();
                thing.MarketTradeAs = r.U16();
                thing.MarketShowAs = r.U16();
                var nameLen5 = r.U16();
                if (nameLen5 > 512) nameLen5 = 512;
                var rawName5 = r.String(nameLen5);
                thing.MarketName = new string(rawName5.Where(c => c >= 0x20 && c <= 0x7E).ToArray());
                thing.MarketRestrictProfession = r.U16();
                thing.MarketRestrictLevel = r.U16();
                break;
        }
    }

    /// <summary>MetadataFlags4 — client 7.80-8.54 (protocol 780-854). HasCharges added; Writable shifted to 0x09.</summary>
    private static void ParseFlagV4(DatReader r, DatThingType thing, int flag)
    {
        switch (flag)
        {
            case 0x00: thing.IsGround = true; thing.GroundSpeed = r.U16(); break;
            case 0x01: thing.IsGroundBorder = true; break;
            case 0x02: thing.IsOnBottom = true; break;
            case 0x03: thing.IsOnTop = true; break;
            case 0x04: thing.IsContainer = true; break;
            case 0x05: thing.IsStackable = true; break;
            case 0x06: thing.IsForceUse = true; break;
            case 0x07: thing.IsMultiUse = true; break;
            case 0x08: thing.HasCharges = true; break;
            case 0x09: thing.IsWritable = true; thing.MaxTextLength = r.U16(); break;
            case 0x0A: thing.IsWritableOnce = true; thing.MaxTextLength = r.U16(); break;
            case 0x0B: thing.IsFluidContainer = true; break;
            case 0x0C: thing.IsFluid = true; break;
            case 0x0D: thing.IsUnpassable = true; break;
            case 0x0E: thing.IsUnmoveable = true; break;
            case 0x0F: thing.IsBlockMissile = true; break;
            case 0x10: thing.IsBlockPathfind = true; break;
            case 0x11: thing.IsPickupable = true; break;
            case 0x12: thing.IsHangable = true; break;
            case 0x13: thing.IsVertical = true; break;
            case 0x14: thing.IsHorizontal = true; break;
            case 0x15: thing.IsRotatable = true; break;
            case 0x16: thing.HasLight = true; thing.LightLevel = r.U16(); thing.LightColor = r.U16(); break;
            case 0x17: thing.IsDontHide = true; break;
            case 0x18: thing.FloorChange = true; break;
            case 0x19: thing.HasOffset = true; thing.OffsetX = r.S16(); thing.OffsetY = r.S16(); break;
            case 0x1A: thing.HasElevation = true; thing.Elevation = r.U16(); break;
            case 0x1B: thing.IsLyingObject = true; break;
            case 0x1C: thing.IsAnimateAlways = true; break;
            case 0x1D: thing.IsMiniMap = true; thing.MiniMapColor = r.U16(); break;
            case 0x1E: thing.IsLensHelp = true; thing.LensHelp = r.U16(); break;
            case 0x1F: thing.IsFullGround = true; break;
            case 0x20: thing.IsIgnoreLook = true; break;
        }
    }

    /// <summary>MetadataFlags3 — client 7.55-7.72 (protocol &lt; 780). No HasCharges; HAS_LIGHT at 0x15.</summary>
    private static void ParseFlagV3(DatReader r, DatThingType thing, int flag)
    {
        switch (flag)
        {
            case 0x00: thing.IsGround = true; thing.GroundSpeed = r.U16(); break;
            case 0x01: thing.IsGroundBorder = true; break;
            case 0x02: thing.IsOnBottom = true; break;
            case 0x03: thing.IsOnTop = true; break;
            case 0x04: thing.IsContainer = true; break;
            case 0x05: thing.IsStackable = true; break;
            case 0x06: thing.IsForceUse = true; break;
            case 0x07: thing.IsMultiUse = true; break;
            case 0x08: thing.IsWritable = true; thing.MaxTextLength = r.U16(); break;
            case 0x09: thing.IsWritableOnce = true; thing.MaxTextLength = r.U16(); break;
            case 0x0A: thing.IsFluidContainer = true; break;
            case 0x0B: thing.IsFluid = true; break;
            case 0x0C: thing.IsUnpassable = true; break;
            case 0x0D: thing.IsUnmoveable = true; break;
            case 0x0E: thing.IsBlockMissile = true; break;
            case 0x0F: thing.IsBlockPathfind = true; break;
            case 0x10: thing.IsPickupable = true; break;
            case 0x11: thing.IsHangable = true; break;
            case 0x12: thing.IsVertical = true; break;
            case 0x13: thing.IsHorizontal = true; break;
            case 0x14: thing.IsRotatable = true; break;
            case 0x15: thing.HasLight = true; thing.LightLevel = r.U16(); thing.LightColor = r.U16(); break;
            case 0x16: break; // Unknown flag in MF3
            case 0x17: thing.FloorChange = true; break;
            case 0x18: thing.HasOffset = true; thing.OffsetX = r.S16(); thing.OffsetY = r.S16(); break;
            case 0x19: thing.HasElevation = true; thing.Elevation = r.U16(); break;
            case 0x1A: thing.IsLyingObject = true; break;
            case 0x1B: thing.IsAnimateAlways = true; break;
            case 0x1C: thing.IsMiniMap = true; thing.MiniMapColor = r.U16(); break;
            case 0x1D: thing.IsLensHelp = true; thing.LensHelp = r.U16(); break;
            case 0x1E: thing.IsFullGround = true; break;
        }
    }

    public static void Save(string path, DatData data)
    {
        var w = new DatWriter();
        int protocol = data.ProtocolVersion;
        bool extended = data.Extended;
        bool frameDurations = data.ImprovedAnimations;
        bool frameGroups = data.FrameGroups;

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
                WriteThing(w, thing, protocol, extended, frameDurations, false);
            else
                WriteEmptyThing(w, ThingCategory.Item, extended, false);
        }

        // Outfits: 1..lastOutfitId (frame groups for outfits only)
        for (int id = 1; id <= lastOutfitId; id++)
        {
            if (data.Outfits.TryGetValue((ushort)id, out var thing))
                WriteThing(w, thing, protocol, extended, frameDurations, frameGroups);
            else
                WriteEmptyThing(w, ThingCategory.Outfit, extended, frameGroups);
        }

        // Effects: 1..lastEffectId (no frame groups)
        for (int id = 1; id <= lastEffectId; id++)
        {
            if (data.Effects.TryGetValue((ushort)id, out var thing))
                WriteThing(w, thing, protocol, extended, frameDurations, false);
            else
                WriteEmptyThing(w, ThingCategory.Effect, extended, false);
        }

        // Missiles: 1..lastMissileId (no frame groups)
        for (int id = 1; id <= lastMissileId; id++)
        {
            if (data.Missiles.TryGetValue((ushort)id, out var thing))
                WriteThing(w, thing, protocol, extended, frameDurations, false);
            else
                WriteEmptyThing(w, ThingCategory.Missile, extended, false);
        }

        File.WriteAllBytes(path, w.ToArray());
    }

    private static void WriteEmptyThing(DatWriter w, ThingCategory category, bool extended, bool frameGroups)
    {
        w.U8(0xFF); // end flags

        // Frame group header (OB: frameGroups && category == OUTFIT)
        bool hasFrameGroups = category == ThingCategory.Outfit && frameGroups;
        if (hasFrameGroups)
        {
            w.U8(1); // 1 frame group
            w.U8((byte)FrameGroupType.Default); // type = Default
        }

        // 1x1, 1 layer, 1x1x1 pattern, 1 frame, 1 sprite (id=0)
        w.U8(1); w.U8(1); // width, height
        w.U8(1); // layers
        w.U8(1); w.U8(1); w.U8(1); // patternX/Y/Z
        w.U8(1); // frames

        // Sprite index size depends on extended flag
        if (extended) w.U32(0); else w.U16(0);
    }

    private static void WriteThing(DatWriter w, DatThingType thing, int protocol,
        bool extended, bool frameDurations, bool frameGroups)
    {
        WriteFlags(w, thing, protocol);

        bool isOutfit = thing.Category == ThingCategory.Outfit;
        bool hasFrameGroups = isOutfit && frameGroups;

        if (hasFrameGroups)
            w.U8((byte)thing.FrameGroups.Length);

        for (int g = 0; g < thing.FrameGroups.Length; g++)
        {
            var fg = thing.FrameGroups[g];
            if (hasFrameGroups)
            {
                // OB quirk: if only 1 group, write type=1; otherwise write loop index
                var groupTypeVal = (byte)(thing.FrameGroups.Length < 2 ? 1 : g);
                w.U8(groupTypeVal);
            }

            w.U8(fg.Width);
            w.U8(fg.Height);
            if (fg.Width > 1 || fg.Height > 1)
                w.U8(fg.ExactSize);

            w.U8(fg.Layers);
            w.U8(fg.PatternX);
            w.U8(fg.PatternY);
            w.U8(fg.PatternZ);
            w.U8(fg.Frames);

            // Enhanced animation data (OB: frameDurations flag, independent of protocol)
            if (fg.Frames > 1 && frameDurations)
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

            // Sprite index size depends on extended flag
            int totalSprites = fg.SpriteCount;
            for (int i = 0; i < totalSprites; i++)
            {
                uint sid = i < fg.SpriteIndex.Length ? fg.SpriteIndex[i] : 0;
                if (extended) w.U32(sid); else w.U16((ushort)sid);
            }
        }
    }

    private static void WriteFlags(DatWriter w, DatThingType t, int protocol)
    {
        // OB uses separate writers: writeItemProperties() for items,
        // writeProperties() for non-items (outfits/effects/missiles).
        if (t.Category == ThingCategory.Item)
        {
            if (protocol <= 772)      WriteFlagsV3(w, t);
            else if (protocol <= 854) WriteFlagsV4(w, t);
            else if (protocol <= 986) WriteFlagsV5(w, t);
            else                      WriteFlagsV6(w, t);
        }
        else
        {
            WriteNonItemFlags(w, t, protocol);
        }
    }

    /// <summary>
    /// OB writeProperties() for non-items: only HAS_LIGHT, HAS_OFFSET, ANIMATE_ALWAYS.
    /// V6 also writes TOP_EFFECT for effects.
    /// </summary>
    private static void WriteNonItemFlags(DatWriter w, DatThingType t, int protocol)
    {
        byte lightFlag, offsetFlag, animAlwaysFlag;
        if (protocol <= 772)      { lightFlag = 0x15; offsetFlag = 0x18; animAlwaysFlag = 0x1B; }
        else if (protocol <= 854) { lightFlag = 0x16; offsetFlag = 0x19; animAlwaysFlag = 0x1C; }
        else if (protocol <= 986) { lightFlag = 0x15; offsetFlag = 0x18; animAlwaysFlag = 0x1B; }
        else                      { lightFlag = 0x16; offsetFlag = 0x19; animAlwaysFlag = 0x1C; }

        if (t.HasLight) { w.U8(lightFlag); w.U16(t.LightLevel); w.U16(t.LightColor); }
        if (t.HasOffset) { w.U8(offsetFlag); w.S16(t.OffsetX); w.S16(t.OffsetY); }
        if (t.IsAnimateAlways) w.U8(animAlwaysFlag);

        // V6 only: TopEffect for effects
        if (protocol > 986 && t.IsTopEffect && t.Category == ThingCategory.Effect)
            w.U8(0x26);

        w.U8(0xFF); // end marker
    }

    /// <summary>MetadataFlags6 — client 10.10+ (protocol &gt; 986). Item flags only.</summary>
    private static void WriteFlagsV6(DatWriter w, DatThingType t)
    {
        // Ground group — mutually exclusive (OB uses if/else-if chain)
        if (t.IsGround) { w.U8(0x00); w.U16(t.GroundSpeed); }
        else if (t.IsGroundBorder) w.U8(0x01);
        else if (t.IsOnBottom) w.U8(0x02);
        else if (t.IsOnTop) w.U8(0x03);
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

    /// <summary>MetadataFlags5 — client 8.60-9.86 (protocol 855-986). Item flags only.</summary>
    private static void WriteFlagsV5(DatWriter w, DatThingType t)
    {
        // Ground group — mutually exclusive (OB uses if/else-if chain)
        if (t.IsGround) { w.U8(0x00); w.U16(t.GroundSpeed); }
        else if (t.IsGroundBorder) w.U8(0x01);
        else if (t.IsOnBottom) w.U8(0x02);
        else if (t.IsOnTop) w.U8(0x03);
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
        // NoMoveAnimation does NOT exist in V5
        if (t.IsPickupable) w.U8(0x10);
        if (t.IsHangable) w.U8(0x11);
        if (t.IsVertical) w.U8(0x12);
        if (t.IsHorizontal) w.U8(0x13);
        if (t.IsRotatable) w.U8(0x14);
        if (t.HasLight) { w.U8(0x15); w.U16(t.LightLevel); w.U16(t.LightColor); }
        if (t.IsDontHide) w.U8(0x16);
        if (t.IsTranslucent) w.U8(0x17);
        if (t.HasOffset) { w.U8(0x18); w.S16(t.OffsetX); w.S16(t.OffsetY); }
        if (t.HasElevation) { w.U8(0x19); w.U16(t.Elevation); }
        if (t.IsLyingObject) w.U8(0x1A);
        if (t.IsAnimateAlways) w.U8(0x1B);
        if (t.IsMiniMap) { w.U8(0x1C); w.U16(t.MiniMapColor); }
        if (t.IsLensHelp) { w.U8(0x1D); w.U16(t.LensHelp); }
        if (t.IsFullGround) w.U8(0x1E);
        if (t.IsIgnoreLook) w.U8(0x1F);
        if (t.IsCloth) { w.U8(0x20); w.U16(t.ClothSlot); }
        if (t.IsMarketItem)
        {
            w.U8(0x21);
            w.U16(t.MarketCategory);
            w.U16(t.MarketTradeAs);
            w.U16(t.MarketShowAs);
            var nameBytes = Encoding.Latin1.GetBytes(t.MarketName ?? string.Empty);
            w.U16((ushort)nameBytes.Length);
            w.Bytes(nameBytes);
            w.U16(t.MarketRestrictProfession);
            w.U16(t.MarketRestrictLevel);
        }
        // DefaultAction, Wrappable, Unwrappable, TopEffect, Usable not in V5
        w.U8(0xFF); // end marker
    }

    /// <summary>MetadataFlags4 — client 7.80-8.54 (protocol 773-854). Item flags only.</summary>
    private static void WriteFlagsV4(DatWriter w, DatThingType t)
    {
        // Ground group — mutually exclusive (OB uses if/else-if chain)
        if (t.IsGround) { w.U8(0x00); w.U16(t.GroundSpeed); }
        else if (t.IsGroundBorder) w.U8(0x01);
        else if (t.IsOnBottom) w.U8(0x02);
        else if (t.IsOnTop) w.U8(0x03);
        if (t.IsContainer) w.U8(0x04);
        if (t.IsStackable) w.U8(0x05);
        if (t.IsForceUse) w.U8(0x06);
        if (t.IsMultiUse) w.U8(0x07);
        if (t.HasCharges) w.U8(0x08);
        if (t.IsWritable) { w.U8(0x09); w.U16(t.MaxTextLength); }
        if (t.IsWritableOnce) { w.U8(0x0A); w.U16(t.MaxTextLength); }
        if (t.IsFluidContainer) w.U8(0x0B);
        if (t.IsFluid) w.U8(0x0C);
        if (t.IsUnpassable) w.U8(0x0D);
        if (t.IsUnmoveable) w.U8(0x0E);
        if (t.IsBlockMissile) w.U8(0x0F);
        if (t.IsBlockPathfind) w.U8(0x10);
        if (t.IsPickupable) w.U8(0x11);
        if (t.IsHangable) w.U8(0x12);
        if (t.IsVertical) w.U8(0x13);
        if (t.IsHorizontal) w.U8(0x14);
        if (t.IsRotatable) w.U8(0x15);
        if (t.HasLight) { w.U8(0x16); w.U16(t.LightLevel); w.U16(t.LightColor); }
        if (t.IsDontHide) w.U8(0x17);
        if (t.FloorChange) w.U8(0x18);
        if (t.HasOffset) { w.U8(0x19); w.S16(t.OffsetX); w.S16(t.OffsetY); }
        if (t.HasElevation) { w.U8(0x1A); w.U16(t.Elevation); }
        if (t.IsLyingObject) w.U8(0x1B);
        if (t.IsAnimateAlways) w.U8(0x1C);
        if (t.IsMiniMap) { w.U8(0x1D); w.U16(t.MiniMapColor); }
        if (t.IsLensHelp) { w.U8(0x1E); w.U16(t.LensHelp); }
        if (t.IsFullGround) w.U8(0x1F);
        if (t.IsIgnoreLook) w.U8(0x20);
        w.U8(0xFF); // end marker
    }

    /// <summary>MetadataFlags3 — client 7.55-7.72 (protocol &lt;= 772). Item flags only.</summary>
    private static void WriteFlagsV3(DatWriter w, DatThingType t)
    {
        // Ground group — mutually exclusive (OB uses if/else-if chain)
        if (t.IsGround) { w.U8(0x00); w.U16(t.GroundSpeed); }
        else if (t.IsGroundBorder) w.U8(0x01);
        else if (t.IsOnBottom) w.U8(0x02);
        else if (t.IsOnTop) w.U8(0x03);
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
        if (t.IsPickupable) w.U8(0x10);
        if (t.IsHangable) w.U8(0x11);
        if (t.IsVertical) w.U8(0x12);
        if (t.IsHorizontal) w.U8(0x13);
        if (t.IsRotatable) w.U8(0x14);
        if (t.HasLight) { w.U8(0x15); w.U16(t.LightLevel); w.U16(t.LightColor); }
        // 0x16 = unknown in V3; skip
        if (t.FloorChange) w.U8(0x17);
        if (t.HasOffset) { w.U8(0x18); w.S16(t.OffsetX); w.S16(t.OffsetY); }
        if (t.HasElevation) { w.U8(0x19); w.U16(t.Elevation); }
        if (t.IsLyingObject) w.U8(0x1A);
        if (t.IsAnimateAlways) w.U8(0x1B);
        if (t.IsMiniMap) { w.U8(0x1C); w.U16(t.MiniMapColor); }
        if (t.IsLensHelp) { w.U8(0x1D); w.U16(t.LensHelp); }
        if (t.IsFullGround) w.U8(0x1E);
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
        public int Position => _pos;

        private void EnsureAvailable(int bytes)
        {
            if (_pos + bytes > data.Length)
                throw new InvalidOperationException(
                    $"DAT reader overrun at offset {_pos}: need {bytes} bytes, only {data.Length - _pos} remaining (total={data.Length})");
        }

        public byte U8()
        {
            EnsureAvailable(1);
            return data[_pos++];
        }

        public sbyte S8()
        {
            EnsureAvailable(1);
            var v = (sbyte)data[_pos];
            _pos++;
            return v;
        }

        public ushort U16()
        {
            EnsureAvailable(2);
            var v = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(_pos));
            _pos += 2;
            return v;
        }

        public short S16()
        {
            EnsureAvailable(2);
            var v = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(_pos));
            _pos += 2;
            return v;
        }

        public uint U32()
        {
            EnsureAvailable(4);
            var v = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(_pos));
            _pos += 4;
            return v;
        }

        public int S32()
        {
            EnsureAvailable(4);
            var v = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(_pos));
            _pos += 4;
            return v;
        }

        public string String(int length)
        {
            EnsureAvailable(length);
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
    public int ProtocolVersion { get; init; }
    /// <summary>True if sprite indices are U32 (extended). False if U16.</summary>
    public bool Extended { get; init; }
    /// <summary>True if enhanced animation data (frame durations) are present.</summary>
    public bool ImprovedAnimations { get; init; }
    /// <summary>True if outfits use frame group headers.</summary>
    public bool FrameGroups { get; init; }
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
