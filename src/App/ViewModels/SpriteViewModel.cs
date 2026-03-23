using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using POriginsItemEditor.OTB;

namespace POriginsItemEditor.App.ViewModels;

/// <summary>
/// VM for a single sprite in the sprite browser list.
/// </summary>
public partial class SpriteViewModel : ObservableObject
{
    [ObservableProperty] private uint _spriteId;
    [ObservableProperty] private WriteableBitmap? _bitmap;

    /// <summary>Flat index into FrameGroup.SpriteIndex[] (set only for composition cells).</summary>
    public int SlotIndex { get; set; } = -1;
}
