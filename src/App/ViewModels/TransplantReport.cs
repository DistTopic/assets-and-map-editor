using POriginsItemEditor.OTB;

namespace POriginsItemEditor.App.ViewModels;

/// <summary>
/// Result of comparing attributes between two protocol versions during transplanting.
/// </summary>
public sealed class TransplantReport
{
    public ushort SourceClientId { get; init; }
    public ushort TargetClientId { get; init; }
    public int SourceProtocol { get; init; }
    public int TargetProtocol { get; init; }

    /// <summary>Attributes that the target version doesn't support (downcast: ignored).</summary>
    public List<string> IgnoredAttributes { get; init; } = [];

    /// <summary>Attributes that the source version doesn't have (upcast: missing).</summary>
    public List<string> MissingAttributes { get; init; } = [];

    /// <summary>Attributes both versions share.</summary>
    public List<string> PreservedAttributes { get; init; } = [];

    public bool HasIssues => IgnoredAttributes.Count > 0 || MissingAttributes.Count > 0;
    public bool IsDowncast => SourceProtocol > TargetProtocol;
    public bool IsUpcast => SourceProtocol < TargetProtocol;

    /// <summary>
    /// Compares a DatThingType from a source protocol to a target protocol's capabilities.
    /// </summary>
    public static TransplantReport Compare(DatThingType source, int sourceProtocol, int targetProtocol)
    {
        var report = new TransplantReport
        {
            SourceClientId = source.Id,
            SourceProtocol = sourceProtocol,
            TargetProtocol = targetProtocol,
        };

        // Attributes available in each protocol tier
        // Tier 1: Protocol <= 860 — basic flags (0x00–0x1C)
        // Tier 2: Protocol >= 1098 — extended flags (0x1D–0x26, 0xFE)

        var tier1Flags = GetActiveFlagsForThing(source, tier: 1);
        var tier2Flags = GetActiveFlagsForThing(source, tier: 2);

        if (sourceProtocol > targetProtocol) // Downcast (e.g., 1098 → 854)
        {
            // Flags only in tier 2 that are set → ignored in target
            foreach (var flag in tier2Flags)
                report.IgnoredAttributes.Add(flag);
            foreach (var flag in tier1Flags)
                report.PreservedAttributes.Add(flag);
        }
        else if (sourceProtocol < targetProtocol) // Upcast (e.g., 854 → 1098)
        {
            // Tier 2 flags the source doesn't have → missing
            report.MissingAttributes.AddRange(GetMissingTier2Flags(source));
            foreach (var flag in tier1Flags)
                report.PreservedAttributes.Add(flag);
        }
        else
        {
            report.PreservedAttributes.AddRange(tier1Flags);
            report.PreservedAttributes.AddRange(tier2Flags);
        }

        return report;
    }

    private static List<string> GetActiveFlagsForThing(DatThingType t, int tier)
    {
        var flags = new List<string>();

        if (tier == 1)
        {
            // Tier 1: flags 0x00–0x1C (all protocols)
            if (t.IsGround) flags.Add("Ground (speed: " + t.GroundSpeed + ")");
            if (t.IsGroundBorder) flags.Add("Ground Border");
            if (t.IsOnBottom) flags.Add("On Bottom");
            if (t.IsOnTop) flags.Add("On Top");
            if (t.IsContainer) flags.Add("Container");
            if (t.IsStackable) flags.Add("Stackable");
            if (t.IsForceUse) flags.Add("Force Use");
            if (t.IsMultiUse) flags.Add("Multi-Use");
            if (t.IsWritable) flags.Add("Writable (max: " + t.MaxTextLength + ")");
            if (t.IsWritableOnce) flags.Add("Writable Once (max: " + t.MaxTextLength + ")");
            if (t.IsFluidContainer) flags.Add("Fluid Container");
            if (t.IsFluid) flags.Add("Fluid");
            if (t.IsUnpassable) flags.Add("Unpassable");
            if (t.IsUnmoveable) flags.Add("Unmoveable");
            if (t.IsBlockMissile) flags.Add("Block Missile");
            if (t.IsBlockPathfind) flags.Add("Block Pathfind");
            if (t.IsNoMoveAnimation) flags.Add("No Move Animation");
            if (t.IsPickupable) flags.Add("Pickupable");
            if (t.IsHangable) flags.Add("Hangable");
            if (t.IsVertical) flags.Add("Vertical");
            if (t.IsHorizontal) flags.Add("Horizontal");
            if (t.IsRotatable) flags.Add("Rotatable");
            if (t.HasLight) flags.Add("Light (level: " + t.LightLevel + ", color: " + t.LightColor + ")");
            if (t.IsDontHide) flags.Add("Don't Hide");
            if (t.IsTranslucent) flags.Add("Translucent");
            if (t.HasOffset) flags.Add("Offset (x: " + t.OffsetX + ", y: " + t.OffsetY + ")");
            if (t.HasElevation) flags.Add("Elevation (" + t.Elevation + ")");
            if (t.IsLyingObject) flags.Add("Lying Object");
            if (t.IsAnimateAlways) flags.Add("Animate Always");
        }
        else if (tier == 2)
        {
            // Tier 2: flags 0x1D–0x26 + 0xFE (protocol >= 1098 only)
            if (t.IsMiniMap) flags.Add("Minimap (color: " + t.MiniMapColor + ")");
            if (t.IsLensHelp) flags.Add("Cursor (" + t.LensHelp + ")");
            if (t.IsFullGround) flags.Add("Full Ground");
            if (t.IsIgnoreLook) flags.Add("Ignore Look");
            if (t.IsCloth) flags.Add("Cloth (slot: " + t.ClothSlot + ")");
            if (t.IsMarketItem) flags.Add("Market Item (" + t.MarketName + ")");
            if (t.HasDefaultAction) flags.Add("Default Action (" + t.DefaultAction + ")");
            if (t.IsWrappable) flags.Add("Wrappable");
            if (t.IsUnwrappable) flags.Add("Unwrappable");
            if (t.IsTopEffect) flags.Add("Top Effect");
            if (t.IsUsable) flags.Add("Usable");
        }

        return flags;
    }

    private static List<string> GetMissingTier2Flags(DatThingType t)
    {
        // When upcasting from 854/860 → 1098, these flags will be missing (default false/0)
        var missing = new List<string>();
        // List all tier-2 flags that are NOT set (they're "missing" since source didn't have them)
        if (!t.IsMiniMap) missing.Add("Minimap");
        if (!t.IsLensHelp) missing.Add("Cursor / Lens Help");
        if (!t.IsFullGround) missing.Add("Full Ground");
        if (!t.IsIgnoreLook) missing.Add("Ignore Look");
        if (!t.IsCloth) missing.Add("Cloth");
        if (!t.IsMarketItem) missing.Add("Market Item");
        if (!t.HasDefaultAction) missing.Add("Default Action");
        if (!t.IsWrappable) missing.Add("Wrappable");
        if (!t.IsUnwrappable) missing.Add("Unwrappable");
        if (!t.IsTopEffect) missing.Add("Top Effect");
        if (!t.IsUsable) missing.Add("Usable");
        return missing;
    }
}
