using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using POriginsItemEditor.OTB;

namespace POriginsItemEditor.App.ViewModels;

/// <summary>
/// VM for a client item (DAT perspective) in the client items list.
/// </summary>
public partial class ClientItemViewModel : ObservableObject
{
    public DatThingType ThingType { get; }

    public ClientItemViewModel(DatThingType thingType)
    {
        ThingType = thingType;
    }

    public ushort Id => ThingType.Id;
    public ThingCategory Category => ThingType.Category;
    public string CategoryName => Category.ToString();
    public uint FirstSpriteId => ThingType.FirstSpriteId;

    [ObservableProperty] private WriteableBitmap? _sprite;

    public int TotalSprites => ThingType.TotalSpriteCount;

    /// <summary>Current animation frame (cycled by timer in MainWindowViewModel).</summary>
    public int AnimFrame { get; set; }

    public int AnimPhases
    {
        get
        {
            if (ThingType.FrameGroups.Length == 0) return 0;
            return ThingType.FrameGroups[0].Frames;
        }
    }

    // ── Frame group info ──
    public byte Width => ThingType.FrameGroups.Length > 0 ? ThingType.FrameGroups[0].Width : (byte)1;
    public byte Height => ThingType.FrameGroups.Length > 0 ? ThingType.FrameGroups[0].Height : (byte)1;
    public byte ExactSize => ThingType.FrameGroups.Length > 0 ? ThingType.FrameGroups[0].ExactSize : (byte)32;
    public byte Layers => ThingType.FrameGroups.Length > 0 ? ThingType.FrameGroups[0].Layers : (byte)1;
    public byte PatternX => ThingType.FrameGroups.Length > 0 ? ThingType.FrameGroups[0].PatternX : (byte)1;
    public byte PatternY => ThingType.FrameGroups.Length > 0 ? ThingType.FrameGroups[0].PatternY : (byte)1;
    public byte PatternZ => ThingType.FrameGroups.Length > 0 ? ThingType.FrameGroups[0].PatternZ : (byte)1;
    public byte Frames => ThingType.FrameGroups.Length > 0 ? ThingType.FrameGroups[0].Frames : (byte)1;
}
