using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AssetsAndMapEditor.App.ViewModels;

/// <summary>
/// Tibia 8-bit color palette (6×6×6 RGB cube = 216 colors).
/// Same formula as Object Builder's ColorUtils.from8Bit().
/// </summary>
public static class TibiaColors
{
    public static Color From8Bit(ushort index)
    {
        if (index >= 216) return Colors.Black;
        byte r = (byte)((index / 36) % 6 * 51);
        byte g = (byte)((index / 6) % 6 * 51);
        byte b = (byte)(index % 6 * 51);
        return Color.FromRgb(r, g, b);
    }

    public static SolidColorBrush BrushFrom8Bit(ushort index)
        => new(From8Bit(index));

    /// <summary>Pre-built palette entries for the picker grid.</summary>
    public static readonly MinimapColorEntry[] Palette = BuildPalette();

    private static MinimapColorEntry[] BuildPalette()
    {
        var entries = new MinimapColorEntry[216];
        for (ushort i = 0; i < 216; i++)
            entries[i] = new MinimapColorEntry(i, From8Bit(i));
        return entries;
    }
}

public partial class MinimapColorEntry : ObservableObject
{
    public ushort Index { get; }
    public Color Color { get; }
    public SolidColorBrush Brush { get; }

    public MinimapColorEntry(ushort index, Color color)
    {
        Index = index;
        Color = color;
        Brush = new SolidColorBrush(color);
    }
}
