namespace AssetsAndMapEditor.OTB;

/// <summary>OTB item-node flag bits (from items.otb 4-byte header per node).</summary>
[Flags]
public enum OtbFlags : uint
{
    None            = 0,
    BlockSolid      = 1 << 0,   // Unpassable
    BlockProjectile = 1 << 1,   // BlockMissiles
    BlockPathFind   = 1 << 2,   // BlockPathfinder
    HasHeight       = 1 << 3,   // HasElevation
    Usable          = 1 << 4,   // MultiUse
    Pickupable      = 1 << 5,
    Moveable        = 1 << 6,
    Stackable       = 1 << 7,
    FloorChangeDown = 1 << 8,
    FloorChangeNorth= 1 << 9,
    FloorChangeEast = 1 << 10,
    FloorChangeSouth= 1 << 11,
    FloorChangeWest = 1 << 12,
    AlwaysOnTop     = 1 << 13,  // StackOrder
    Readable        = 1 << 14,
    Rotatable       = 1 << 15,
    Hangable        = 1 << 16,
    Vertical        = 1 << 17,  // HookSouth
    Horizontal      = 1 << 18,  // HookEast
    CanNotDecay     = 1 << 19,
    AllowDistRead   = 1 << 20,
    Unused          = 1 << 21,
    ClientCharges   = 1 << 22,
    LookThrough     = 1 << 23,  // IgnoreLook
    Animation       = 1 << 24,  // IsAnimation
    FullGround      = 1 << 25,
    ForceUse        = 1 << 26,
}

/// <summary>OTB attribute type byte.</summary>
public enum OtbAttribute : byte
{
    ServerId         = 0x10,
    ClientId         = 0x11,
    Name             = 0x12,
    Speed            = 0x14,
    SpriteHash       = 0x20,
    MinimapColor     = 0x21,
    MaxReadWriteChars= 0x22,
    MaxReadChars     = 0x23,
    Light2           = 0x2A,
    TopOrder         = 0x2B,
    WareId           = 0x2D,
}

/// <summary>OTB item-group byte (node type).</summary>
public enum OtbGroup : byte
{
    None       = 0,
    Ground     = 1,
    Container  = 2,
    Weapon     = 3,
    Ammunition = 4,
    Armor      = 5,
    Charges    = 6,
    Teleport   = 7,
    MagicField = 8,
    Writable   = 9,
    Key        = 10,
    Splash     = 11,
    Fluid      = 12,
    Door       = 13,
    Deprecated = 14,
    PodiumOfTenacity = 15,
    Last       = 16,
}
