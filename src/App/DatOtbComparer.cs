using System;
using System.Collections.Generic;
using AssetsAndMapEditor.OTB;
using AssetsAndMapEditor.App.ViewModels;

namespace AssetsAndMapEditor.App;

/// <summary>
/// Compares DAT item properties with OTB item properties and produces a list
/// of <see cref="DivergentItem"/>s for items that have mismatched values.
/// Each difference carries actions for both directions (DAT→OTB and OTB→DAT).
/// </summary>
public static class DatOtbComparer
{
    public static List<DivergentItem> FindDivergentItems(OtbData otbData, DatData datData)
    {
        var result = new List<DivergentItem>();

        foreach (var otb in otbData.Items)
        {
            if (otb.ClientId == 0) continue;
            if (!datData.Items.TryGetValue(otb.ClientId, out var dat)) continue;

            var diffs = CompareItem(otb, dat);
            if (diffs.Count > 0)
                result.Add(new DivergentItem { OtbItem = otb, DatThing = dat, Differences = diffs });
        }

        return result;
    }

    private static List<PropertyDifference> CompareItem(OtbItem otb, DatThingType dat)
    {
        var diffs = new List<PropertyDifference>();

        // ── Flag mappings ───────────────────────────────────────────────

        CompareFlag(diffs, "BlockSolid (Unpassable)",
            otb, OtbFlags.BlockSolid,
            dat, d => d.IsUnpassable, (d, v) => d.IsUnpassable = v);

        CompareFlag(diffs, "BlockProjectile (BlockMissile)",
            otb, OtbFlags.BlockProjectile,
            dat, d => d.IsBlockMissile, (d, v) => d.IsBlockMissile = v);

        CompareFlag(diffs, "BlockPathFind",
            otb, OtbFlags.BlockPathFind,
            dat, d => d.IsBlockPathfind, (d, v) => d.IsBlockPathfind = v);

        CompareFlag(diffs, "HasHeight (Elevation)",
            otb, OtbFlags.HasHeight,
            dat, d => d.HasElevation, (d, v) => d.HasElevation = v);

        CompareFlag(diffs, "Usable (MultiUse)",
            otb, OtbFlags.Usable,
            dat, d => d.IsMultiUse, (d, v) => d.IsMultiUse = v);

        CompareFlag(diffs, "Pickupable",
            otb, OtbFlags.Pickupable,
            dat, d => d.IsPickupable, (d, v) => d.IsPickupable = v);

        // Moveable is inverted: OTB Moveable ↔ DAT !IsUnmoveable
        {
            bool otbVal = otb.Flags.HasFlag(OtbFlags.Moveable);
            bool datVal = !dat.IsUnmoveable;
            if (otbVal != datVal)
            {
                diffs.Add(new PropertyDifference
                {
                    PropertyName = "Moveable (!Unmoveable)",
                    OtbValue = otbVal.ToString(),
                    DatValue = datVal.ToString(),
                    ApplyToOtb = o => SetFlag(o, OtbFlags.Moveable, datVal),
                    ApplyToDat = d => d.IsUnmoveable = !otbVal,
                });
            }
        }

        CompareFlag(diffs, "Stackable",
            otb, OtbFlags.Stackable,
            dat, d => d.IsStackable, (d, v) => d.IsStackable = v);

        CompareFlag(diffs, "Readable (Writable)",
            otb, OtbFlags.Readable,
            dat, d => d.IsWritable, (d, v) => d.IsWritable = v);

        CompareFlag(diffs, "Rotatable",
            otb, OtbFlags.Rotatable,
            dat, d => d.IsRotatable, (d, v) => d.IsRotatable = v);

        CompareFlag(diffs, "Hangable",
            otb, OtbFlags.Hangable,
            dat, d => d.IsHangable, (d, v) => d.IsHangable = v);

        CompareFlag(diffs, "Vertical (HookSouth)",
            otb, OtbFlags.Vertical,
            dat, d => d.IsVertical, (d, v) => d.IsVertical = v);

        CompareFlag(diffs, "Horizontal (HookEast)",
            otb, OtbFlags.Horizontal,
            dat, d => d.IsHorizontal, (d, v) => d.IsHorizontal = v);

        CompareFlag(diffs, "ClientCharges",
            otb, OtbFlags.ClientCharges,
            dat, d => d.HasCharges, (d, v) => d.HasCharges = v);

        CompareFlag(diffs, "LookThrough (IgnoreLook)",
            otb, OtbFlags.LookThrough,
            dat, d => d.IsIgnoreLook, (d, v) => d.IsIgnoreLook = v);

        CompareFlag(diffs, "FullGround",
            otb, OtbFlags.FullGround,
            dat, d => d.IsFullGround, (d, v) => d.IsFullGround = v);

        CompareFlag(diffs, "ForceUse",
            otb, OtbFlags.ForceUse,
            dat, d => d.IsForceUse, (d, v) => d.IsForceUse = v);

        CompareFlag(diffs, "Animation",
            otb, OtbFlags.Animation,
            dat, d => d.IsAnimateAlways, (d, v) => d.IsAnimateAlways = v);

        // FloorChange — DAT has a single bool, OTB has directional flags
        {
            bool otbHasFloorChange = otb.Flags.HasFlag(OtbFlags.FloorChangeDown)
                || otb.Flags.HasFlag(OtbFlags.FloorChangeNorth)
                || otb.Flags.HasFlag(OtbFlags.FloorChangeEast)
                || otb.Flags.HasFlag(OtbFlags.FloorChangeSouth)
                || otb.Flags.HasFlag(OtbFlags.FloorChangeWest);

            if (otbHasFloorChange != dat.FloorChange)
            {
                diffs.Add(new PropertyDifference
                {
                    PropertyName = "FloorChange",
                    OtbValue = otbHasFloorChange.ToString(),
                    DatValue = dat.FloorChange.ToString(),
                    ApplyToOtb = o =>
                    {
                        const OtbFlags allFloor = OtbFlags.FloorChangeDown | OtbFlags.FloorChangeNorth
                            | OtbFlags.FloorChangeEast | OtbFlags.FloorChangeSouth | OtbFlags.FloorChangeWest;
                        if (dat.FloorChange)
                            o.Flags |= OtbFlags.FloorChangeDown;
                        else
                            o.Flags &= ~allFloor;
                    },
                    ApplyToDat = d => d.FloorChange = otbHasFloorChange,
                });
            }
        }

        // ── Value attributes ────────────────────────────────────────────

        CompareValue(diffs, "Speed (GroundSpeed)",
            otb.Speed, dat.GroundSpeed,
            o => o.Speed = dat.GroundSpeed,
            d => d.GroundSpeed = otb.Speed);

        CompareValue(diffs, "LightLevel",
            otb.LightLevel, dat.LightLevel,
            o => o.LightLevel = dat.LightLevel,
            d => d.LightLevel = otb.LightLevel);

        CompareColorValue(diffs, "LightColor",
            otb.LightColor, dat.LightColor,
            o => o.LightColor = dat.LightColor,
            d => d.LightColor = otb.LightColor);

        CompareColorValue(diffs, "MinimapColor",
            otb.MinimapColor, dat.MiniMapColor,
            o => o.MinimapColor = dat.MiniMapColor,
            d => d.MiniMapColor = otb.MinimapColor);

        if (dat.IsWritable || dat.IsWritableOnce)
        {
            CompareValue(diffs, "MaxReadWriteChars (MaxTextLength)",
                otb.MaxReadWriteChars, dat.MaxTextLength,
                o => o.MaxReadWriteChars = dat.MaxTextLength,
                d => d.MaxTextLength = otb.MaxReadWriteChars);
        }

        return diffs;
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static void CompareFlag(
        List<PropertyDifference> diffs,
        string name,
        OtbItem otb, OtbFlags flag,
        DatThingType dat, Func<DatThingType, bool> getDat, Action<DatThingType, bool> setDat)
    {
        bool otbVal = otb.Flags.HasFlag(flag);
        bool datVal = getDat(dat);
        if (otbVal == datVal) return;
        diffs.Add(new PropertyDifference
        {
            PropertyName = name,
            OtbValue = otbVal.ToString(),
            DatValue = datVal.ToString(),
            ApplyToOtb = o => SetFlag(o, flag, datVal),
            ApplyToDat = d => setDat(d, otbVal),
        });
    }

    private static void CompareValue(
        List<PropertyDifference> diffs,
        string name,
        ushort otbValue,
        ushort datValue,
        Action<OtbItem> applyToOtb,
        Action<DatThingType> applyToDat)
    {
        if (otbValue == datValue) return;
        diffs.Add(new PropertyDifference
        {
            PropertyName = name,
            OtbValue = otbValue.ToString(),
            DatValue = datValue.ToString(),
            ApplyToOtb = applyToOtb,
            ApplyToDat = applyToDat,
        });
    }

    private static void CompareColorValue(
        List<PropertyDifference> diffs,
        string name,
        ushort otbValue,
        ushort datValue,
        Action<OtbItem> applyToOtb,
        Action<DatThingType> applyToDat)
    {
        if (otbValue == datValue) return;
        diffs.Add(new PropertyDifference
        {
            PropertyName = name,
            OtbValue = otbValue.ToString(),
            DatValue = datValue.ToString(),
            ApplyToOtb = applyToOtb,
            ApplyToDat = applyToDat,
            IsColor = true,
            OtbColor = TibiaColors.From8Bit(otbValue),
            DatColor = TibiaColors.From8Bit(datValue),
        });
    }

    private static void SetFlag(OtbItem otb, OtbFlags flag, bool value)
    {
        otb.Flags = value ? otb.Flags | flag : otb.Flags & ~flag;
    }
}
