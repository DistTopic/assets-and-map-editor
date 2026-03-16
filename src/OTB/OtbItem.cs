namespace POriginsItemEditor.OTB;

/// <summary>Represents a single item entry parsed from items.otb.</summary>
public sealed class OtbItem
{
    public OtbGroup Group { get; set; }
    public OtbFlags Flags { get; set; }
    public ushort ServerId { get; set; }
    public ushort ClientId { get; set; }
    public ushort Speed { get; set; }
    public ushort WareId { get; set; }
    public ushort LightLevel { get; set; }
    public ushort LightColor { get; set; }
    public byte TopOrder { get; set; }
    public ushort MinimapColor { get; set; }
    public ushort MaxReadWriteChars { get; set; }
    public ushort MaxReadChars { get; set; }
    public byte[]? SpriteHash { get; set; }
    public string? Name { get; set; }

    /// <summary>Raw attribute bytes that we don't explicitly parse — preserved for lossless save.</summary>
    public List<(byte Type, byte[] Data)> UnknownAttributes { get; } = [];

    // ── Convenience flag accessors ──────────────────────────────────────

    public bool IsAnimation
    {
        get => Flags.HasFlag(OtbFlags.Animation);
        set => Flags = value ? Flags | OtbFlags.Animation : Flags & ~OtbFlags.Animation;
    }

    public bool IsStackable => Flags.HasFlag(OtbFlags.Stackable);
    public bool IsPickupable => Flags.HasFlag(OtbFlags.Pickupable);
    public bool IsMoveable => Flags.HasFlag(OtbFlags.Moveable);
    public bool IsBlockSolid => Flags.HasFlag(OtbFlags.BlockSolid);
    public bool IsBlockProjectile => Flags.HasFlag(OtbFlags.BlockProjectile);
    public bool IsHasHeight => Flags.HasFlag(OtbFlags.HasHeight);
    public bool IsUsable => Flags.HasFlag(OtbFlags.Usable);
    public bool IsHangable => Flags.HasFlag(OtbFlags.Hangable);
    public bool IsRotatable => Flags.HasFlag(OtbFlags.Rotatable);
    public bool IsReadable => Flags.HasFlag(OtbFlags.Readable);
    public bool IsLookThrough => Flags.HasFlag(OtbFlags.LookThrough);
    public bool IsForceUse => Flags.HasFlag(OtbFlags.ForceUse);
    public bool IsFullGround => Flags.HasFlag(OtbFlags.FullGround);
    public bool IsClientCharges => Flags.HasFlag(OtbFlags.ClientCharges);
}
