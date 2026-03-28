namespace AssetsAndMapEditor.OTB;

/// <summary>
/// Represents a complete thing (item/outfit/effect/missile) loaded from Tibia.dat.
/// Protocol 1098 — MetadataFlags6, extended sprites (U32), improved animations, frame groups.
/// </summary>
public sealed class DatThingType
{
    public ushort Id { get; set; }
    public ThingCategory Category { get; set; }

    // ── Flags (boolean) ──

    public bool IsGround { get; set; }
    public bool IsGroundBorder { get; set; }
    public bool IsOnBottom { get; set; }
    public bool IsOnTop { get; set; }
    public bool IsContainer { get; set; }
    public bool IsStackable { get; set; }
    public bool IsForceUse { get; set; }
    public bool IsMultiUse { get; set; }
    public bool IsWritable { get; set; }
    public bool IsWritableOnce { get; set; }
    public bool IsFluidContainer { get; set; }
    public bool IsFluid { get; set; }
    public bool IsUnpassable { get; set; }
    public bool IsUnmoveable { get; set; }
    public bool IsBlockMissile { get; set; }
    public bool IsBlockPathfind { get; set; }
    public bool IsNoMoveAnimation { get; set; }
    public bool IsPickupable { get; set; }
    public bool IsHangable { get; set; }
    public bool IsVertical { get; set; }
    public bool IsHorizontal { get; set; }
    public bool IsRotatable { get; set; }
    public bool HasLight { get; set; }
    public bool IsDontHide { get; set; }
    public bool IsTranslucent { get; set; }
    public bool HasOffset { get; set; }
    public bool HasElevation { get; set; }
    public bool IsLyingObject { get; set; }
    public bool IsAnimateAlways { get; set; }
    public bool IsMiniMap { get; set; }
    public bool IsLensHelp { get; set; }
    public bool IsFullGround { get; set; }
    public bool IsIgnoreLook { get; set; }
    public bool IsCloth { get; set; }
    public bool IsMarketItem { get; set; }
    public bool HasDefaultAction { get; set; }
    public bool IsWrappable { get; set; }
    public bool IsUnwrappable { get; set; }
    public bool IsTopEffect { get; set; }
    public bool IsUsable { get; set; }

    // ── Flag data values ──

    public ushort GroundSpeed { get; set; }
    public ushort MaxTextLength { get; set; }
    public ushort LightLevel { get; set; }
    public ushort LightColor { get; set; }
    public short OffsetX { get; set; }
    public short OffsetY { get; set; }
    public ushort Elevation { get; set; }
    public ushort MiniMapColor { get; set; }
    public ushort LensHelp { get; set; }
    public ushort ClothSlot { get; set; }
    public ushort DefaultAction { get; set; }

    // Market data
    public ushort MarketCategory { get; set; }
    public ushort MarketTradeAs { get; set; }
    public ushort MarketShowAs { get; set; }
    public string MarketName { get; set; } = string.Empty;
    public ushort MarketRestrictProfession { get; set; }
    public ushort MarketRestrictLevel { get; set; }

    // ── ComboBox index helpers (int ↔ ushort for SelectedIndex binding) ──

    public int ClothSlotIndex
    {
        get => ClothSlot;
        set => ClothSlot = (ushort)Math.Clamp(value, 0, 10);
    }

    public int DefaultActionIndex
    {
        get => DefaultAction;
        set => DefaultAction = (ushort)Math.Clamp(value, 0, 4);
    }

    public int LensHelpIndex
    {
        get => Math.Max(0, LensHelp - 1100);
        set => LensHelp = (ushort)(value + 1100);
    }

    public int MarketCategoryIndex
    {
        get => MarketCategory == 0 ? 8 : Math.Clamp(MarketCategory - 1, 0, 22);
        set => MarketCategory = (ushort)(value + 1);
    }

    /// <summary>Creates a deep copy of this thing type.</summary>
    public DatThingType Clone()
    {
        var c = (DatThingType)MemberwiseClone();
        c.MarketName = MarketName; // string is immutable, but keep explicit
        c.FrameGroups = new FrameGroup[FrameGroups.Length];
        for (int i = 0; i < FrameGroups.Length; i++)
            c.FrameGroups[i] = FrameGroups[i].Clone();
        return c;
    }

    // ── Frame groups ──

    public FrameGroup[] FrameGroups { get; set; } = [];

    /// <summary>Total sprite count across all frame groups.</summary>
    public int TotalSpriteCount
    {
        get
        {
            int total = 0;
            foreach (var fg in FrameGroups)
                total += fg.SpriteCount;
            return total;
        }
    }

    /// <summary>The first sprite ID (from first frame group), or 0.</summary>
    public uint FirstSpriteId =>
        FrameGroups.Length > 0 && FrameGroups[0].SpriteIndex.Length > 0
            ? FrameGroups[0].SpriteIndex[0]
            : 0;
}

public enum ThingCategory : byte
{
    Item = 1,
    Outfit = 2,
    Effect = 3,
    Missile = 4,
}

/// <summary>
/// A frame group contains the sprite layout and animation data for a thing.
/// Items have 1 group (DEFAULT). Outfits can have 2 (DEFAULT + WALKING).
/// </summary>
public sealed class FrameGroup
{
    public FrameGroupType Type { get; set; }
    public byte Width { get; set; } = 1;
    public byte Height { get; set; } = 1;
    public byte ExactSize { get; set; } = 32;
    public byte Layers { get; set; } = 1;
    public byte PatternX { get; set; } = 1;
    public byte PatternY { get; set; } = 1;
    public byte PatternZ { get; set; } = 1;
    public byte Frames { get; set; } = 1;

    // ── Animation (when Frames > 1) ──

    public bool IsAnimation => Frames > 1;
    public AnimationMode AnimationMode { get; set; }
    public int LoopCount { get; set; }
    public sbyte StartFrame { get; set; }
    public FrameDuration[] FrameDurations { get; set; } = [];

    /// <summary>Total number of sprite slots in this group.</summary>
    public int SpriteCount => Width * Height * Layers * PatternX * PatternY * PatternZ * Frames;

    /// <summary>
    /// Resizes SpriteIndex and FrameDurations to match the current layout dimensions,
    /// preserving existing data (matching Object Builder behavior).
    /// </summary>
    public void UpdateSpriteCount()
    {
        int count = SpriteCount;
        if (SpriteIndex.Length != count)
            Array.Resize(ref _spriteIndex, count);

        if (Frames > 1)
        {
            var oldDurations = FrameDurations;
            var newDurations = new FrameDuration[Frames];
            for (int i = 0; i < Frames; i++)
                newDurations[i] = (oldDurations != null && i < oldDurations.Length)
                    ? oldDurations[i]
                    : new FrameDuration { Minimum = 100, Maximum = 100 };
            FrameDurations = newDurations;
        }
    }

    private uint[] _spriteIndex = [];

    /// <summary>All sprite IDs in layout order. Length = W * H * L * PX * PY * PZ * Frames.</summary>
    public uint[] SpriteIndex
    {
        get => _spriteIndex;
        set => _spriteIndex = value;
    }

    /// <summary>
    /// Get sprite index at a specific position in the layout.
    /// Order: ((((((frame * patternZ + pZ) * patternY + pY) * patternX + pX) * layers + layer) * height + h) * width + w)
    /// </summary>
    public uint GetSpriteId(int w, int h, int layer, int patternX, int patternY, int patternZ, int frame)
    {
        if (Width == 0 || Height == 0 || Layers == 0 || PatternX == 0 || PatternY == 0 || PatternZ == 0 || Frames == 0)
            return 0;
        int index = ((((((frame % Frames) * PatternZ + patternZ) * PatternY + patternY) * PatternX + patternX)
                     * Layers + layer) * Height + h) * Width + w;
        return index < SpriteIndex.Length ? SpriteIndex[index] : 0;
    }

    /// <summary>Compute the flat index for a given position (same formula as GetSpriteId).</summary>
    public int GetFlatIndex(int w, int h, int layer, int patternX, int patternY, int patternZ, int frame)
    {
        if (Width == 0 || Height == 0 || Layers == 0 || PatternX == 0 || PatternY == 0 || PatternZ == 0 || Frames == 0)
            return -1;
        int index = ((((((frame % Frames) * PatternZ + patternZ) * PatternY + patternY) * PatternX + patternX)
                     * Layers + layer) * Height + h) * Width + w;
        return index < SpriteIndex.Length ? index : -1;
    }

    /// <summary>Set sprite ID at a flat index position.</summary>
    public void SetSpriteId(int flatIndex, uint spriteId)
    {
        if (flatIndex >= 0 && flatIndex < SpriteIndex.Length)
            SpriteIndex[flatIndex] = spriteId;
    }

    /// <summary>Creates a deep copy of this frame group.</summary>
    public FrameGroup Clone()
    {
        var c = (FrameGroup)MemberwiseClone();
        c._spriteIndex = (uint[])_spriteIndex.Clone();
        if (FrameDurations.Length > 0)
        {
            c.FrameDurations = new FrameDuration[FrameDurations.Length];
            for (int i = 0; i < FrameDurations.Length; i++)
                c.FrameDurations[i] = FrameDurations[i].Clone();
        }
        return c;
    }
}

public enum FrameGroupType : byte
{
    Default = 0,
    Walking = 1,
}

public enum AnimationMode : byte
{
    Async = 0,
    Sync = 1,
}

/// <summary>Duration range for a single animation frame (milliseconds).</summary>
public sealed class FrameDuration
{
    public uint Minimum { get; set; }
    public uint Maximum { get; set; }

    public uint Duration => Minimum == Maximum ? Minimum : Minimum + (uint)(Random.Shared.Next((int)(Maximum - Minimum)));

    public FrameDuration Clone() => new() { Minimum = Minimum, Maximum = Maximum };
}
