using System.Buffers.Binary;
using System.Text;

namespace AssetsAndMapEditor.OTB;

/// <summary>
/// Tibia.dat parser supporting multiple protocol versions.
/// Supports MetadataFlags6 (1098+), and legacy protocols (854, 860).
/// </summary>
public static class DatFile
{
    /// <summary>Optional log callback for diagnostic messages during load.</summary>
    public static Action<string>? DiagLog { get; set; }

    public static DatData Load(string path, int protocolHint = 0)
    {
        var raw = File.ReadAllBytes(path);
        var sig = BinaryPrimitives.ReadUInt32LittleEndian(raw);
        int detected = DetectProtocol(sig);

        DiagLog?.Invoke($"[DAT] File={Path.GetFileName(path)}, size={raw.Length}, sig=0x{sig:X8}, detectedProto={detected}, hint={protocolHint}");

        int primary = protocolHint > 0 ? protocolHint : detected;

        // Build protocol list: primary first, then fallbacks.
        var protocols = new List<int> { primary };
        int[] allProtocols = [1098, 1076, 1057, 1050, 960, 860, 854, 810, 800, 790, 780, 770, 760, 750, 740];
        foreach (var p in allProtocols)
            if (p != primary) protocols.Add(p);

        // Feature flag combinations (like PStory's tryLoadDatWithFallbacks):
        // extended (U32 sprites), enhancedAnimations, frameGroups — all independent.
        var featureCombos = new (bool ext, bool anim, bool fg)[]
        {
            (false, false, false), // version-default for <=854
            (true,  false, false), // SpritesU32 only
            (true,  true,  false), // SpritesU32 + EnhancedAnimations
            (true,  true,  true),  // SpritesU32 + EnhancedAnimations + IdleAnimations
            (false, true,  false), // EnhancedAnimations only
            (false, false, true),  // IdleAnimations only
            (false, true,  true),  // EnhancedAnimations + IdleAnimations
            (true,  false, true),  // SpritesU32 + IdleAnimations
        };

        DatData? bestResult = null;
        int bestTotal = -1;

        foreach (var proto in protocols)
        {
            foreach (var (ext, anim, fg) in featureCombos)
            {
                DatData result;
                try
                {
                    result = Parse(raw, proto, ext, anim, fg);
                }
                catch (Exception ex)
                {
                    DiagLog?.Invoke($"[DAT] Parse FAILED: proto={proto}, ext={ext}, anim={anim}, fg={fg}: {ex.Message}");
                    continue;
                }

                int total = result.Items.Count + result.Outfits.Count + result.Effects.Count + result.Missiles.Count;
                int expected = result.ItemCount + result.OutfitCount + result.EffectCount + result.MissileCount;

                DiagLog?.Invoke($"[DAT] Parse OK: proto={proto}, ext={ext}, anim={anim}, fg={fg}, things={total}/{expected} (items={result.Items.Count}, outfits={result.Outfits.Count}, effects={result.Effects.Count}, missiles={result.Missiles.Count})");

                // Perfect parse — return immediately
                if (total == expected)
                    return result;

                // Track the best partial result
                if (total > bestTotal)
                {
                    bestTotal = total;
                    bestResult = result;
                }
            }
        }

        if (bestResult != null)
        {
            DiagLog?.Invoke($"[DAT] Using best partial result: {bestTotal} things parsed.");
            return bestResult;
        }

        throw new InvalidOperationException(
            $"Failed to parse {Path.GetFileName(path)} (sig=0x{sig:X8}, size={raw.Length}). No protocol/extended combination worked.");
    }

    /// <summary>
    /// Detects the protocol version from the DAT signature.
    /// Known signatures map to specific versions; unknown defaults to 1098.
    /// </summary>
    public static int DetectProtocol(uint signature)
    {
        // Well-known Tibia.dat signatures (from Object Builder / OTClient sources)
        return signature switch
        {
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
            _ => 1098,
        };
    }

    private static DatData Parse(byte[] raw, int protocolHint, bool extended, bool enhancedAnimations, bool frameGroups)
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
                var thing = ParseThing(r, (ushort)id, ThingCategory.Item, protocol, extended, enhancedAnimations, frameGroups);
                items[(ushort)id] = thing;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed parsing Item {id}/{lastItemId} at reader offset {r.Position} (protocol {protocol}): {ex.Message}", ex);
            }
        }

        // Parse outfits/effects/missiles independently — each category gets its own
        // try/catch so a failure in one doesn't blank out the others.
        // Partial results are KEPT (e.g. 4970/5030 outfits is better than 0).

        try
        {
            for (int id = 1; id <= numOutfits; id++)
                outfits[(ushort)id] = ParseThing(r, (ushort)id, ThingCategory.Outfit, protocol, extended, enhancedAnimations, frameGroups);
            DiagLog?.Invoke($"[DAT] Outfits OK: {outfits.Count}/{numOutfits}, readerPos={r.Position}");
        }
        catch (Exception ex)
        {
            DiagLog?.Invoke($"[DAT] Outfits FAILED at {outfits.Count}/{numOutfits}, readerPos={r.Position}: {ex.Message}");
        }

        try
        {
            for (int id = 1; id <= numEffects; id++)
                effects[(ushort)id] = ParseThing(r, (ushort)id, ThingCategory.Effect, protocol, extended, enhancedAnimations, frameGroups);
            DiagLog?.Invoke($"[DAT] Effects OK: {effects.Count}/{numEffects}, readerPos={r.Position}");
        }
        catch (Exception ex)
        {
            DiagLog?.Invoke($"[DAT] Effects FAILED at {effects.Count}/{numEffects}, readerPos={r.Position}: {ex.Message}");
        }

        try
        {
            for (int id = 1; id <= numMissiles; id++)
                missiles[(ushort)id] = ParseThing(r, (ushort)id, ThingCategory.Missile, protocol, extended, enhancedAnimations, frameGroups);
            DiagLog?.Invoke($"[DAT] Missiles OK: {missiles.Count}/{numMissiles}, readerPos={r.Position}");
        }
        catch (Exception ex)
        {
            DiagLog?.Invoke($"[DAT] Missiles FAILED at {missiles.Count}/{numMissiles}, readerPos={r.Position}: {ex.Message}");
        }

        // If ALL secondary categories failed AND most of the file is unread,
        // the protocol is almost certainly wrong — reject so fallback tries the next one.
        bool secondaryFailed = outfits.Count == 0 && effects.Count == 0 && missiles.Count == 0
                               && (numOutfits + numEffects + numMissiles) > 0;
        if (secondaryFailed)
        {
            int remaining = raw.Length - r.Position;
            int expectedSecondary = (numOutfits + numEffects + numMissiles) * 10; // rough minimum bytes
            if (remaining > expectedSecondary / 2 && remaining > 1000)
                throw new InvalidOperationException(
                    $"Secondary categories failed with {remaining} bytes remaining (protocol {protocol}, ext={extended}) — likely wrong protocol.");
        }

        return new DatData
        {
            Signature = signature,
            ProtocolVersion = protocol,
            Extended = extended,
            EnhancedAnimations = enhancedAnimations,
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

    private static DatThingType ParseThing(DatReader r, ushort id, ThingCategory category, int protocol, bool extended, bool enhancedAnimations, bool frameGroups)
    {
        var thing = new DatThingType { Id = id, Category = category };

        // ── Parse flags ──
        ParseFlags(r, thing, protocol);

        // ── Parse frame groups ──
        // Frame groups only apply to outfits/creatures when the feature is enabled.
        bool isOutfit = category == ThingCategory.Outfit;
        int groupCount = (isOutfit && frameGroups) ? r.U8() : 1;
        var groups = new FrameGroup[groupCount];

        for (int g = 0; g < groupCount; g++)
        {
            var fg = new FrameGroup();
            if (isOutfit && frameGroups)
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

            // Enhanced/improved animations — independent feature flag
            if (fg.Frames > 1 && enhancedAnimations)
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
            // MetadataFlags3: client 7.55-7.72  (protocol < 780)
            // MetadataFlags4: client 7.80-8.54  (protocol 780-854)
            // MetadataFlags5: client 8.60-9.86  (protocol 860-1009)
            // MetadataFlags6: client 10.10+     (protocol >= 1010)
            if (protocol < 780)
                ParseFlagV3(r, thing, flag);
            else if (protocol < 860)
                ParseFlagV4(r, thing, flag);
            else if (protocol < 1010)
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
            case 0x08: break; // HasCharges — boolean only, no data, no DatThingType field
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
            case 0x18: break; // FloorChange — boolean only
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
            case 0x17: break; // FloorChange — boolean only
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
                WriteThing(w, thing, data.Extended, data.EnhancedAnimations, data.FrameGroups);
            else
                WriteEmptyThing(w, data.Extended);
        }

        // Outfits: 1..lastOutfitId
        for (int id = 1; id <= lastOutfitId; id++)
        {
            if (data.Outfits.TryGetValue((ushort)id, out var thing))
                WriteThing(w, thing, data.Extended, data.EnhancedAnimations, data.FrameGroups);
            else
                WriteEmptyThing(w, data.Extended);
        }

        // Effects: 1..lastEffectId
        for (int id = 1; id <= lastEffectId; id++)
        {
            if (data.Effects.TryGetValue((ushort)id, out var thing))
                WriteThing(w, thing, data.Extended, data.EnhancedAnimations, data.FrameGroups);
            else
                WriteEmptyThing(w, data.Extended);
        }

        // Missiles: 1..lastMissileId
        for (int id = 1; id <= lastMissileId; id++)
        {
            if (data.Missiles.TryGetValue((ushort)id, out var thing))
                WriteThing(w, thing, data.Extended, data.EnhancedAnimations, data.FrameGroups);
            else
                WriteEmptyThing(w, data.Extended);
        }

        File.WriteAllBytes(path, w.ToArray());
    }

    private static void WriteEmptyThing(DatWriter w, bool extended)
    {
        w.U8(0xFF); // end flags
        // 1 frame group: 1x1, 1 layer, 1x1x1 pattern, 1 frame, 1 sprite (id=0)
        w.U8(1); w.U8(1); // width, height
        w.U8(1); // layers
        w.U8(1); w.U8(1); w.U8(1); // patternX/Y/Z
        w.U8(1); // frames
        if (extended) w.U32(0); else w.U16(0); // sprite id
    }

    private static void WriteThing(DatWriter w, DatThingType thing, bool extended, bool enhancedAnimations, bool frameGroups)
    {
        WriteFlags(w, thing);

        bool isOutfit = thing.Category == ThingCategory.Outfit;
        if (isOutfit && frameGroups)
            w.U8((byte)thing.FrameGroups.Length);

        for (int g = 0; g < thing.FrameGroups.Length; g++)
        {
            var fg = thing.FrameGroups[g];
            if (isOutfit && frameGroups)
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

            if (fg.Frames > 1 && enhancedAnimations)
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
            {
                uint sid = i < fg.SpriteIndex.Length ? fg.SpriteIndex[i] : 0;
                if (extended) w.U32(sid); else w.U16((ushort)sid);
            }
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
        public int Position => _pos;

        public void Seek(int position) => _pos = position;

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
    /// <summary>True if enhanced animation durations are present in the DAT.</summary>
    public bool EnhancedAnimations { get; init; }
    /// <summary>True if outfit/creature frame groups are present in the DAT.</summary>
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
