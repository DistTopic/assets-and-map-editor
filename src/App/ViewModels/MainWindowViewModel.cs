using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using POriginsItemEditor.OTB;
using System.Collections.ObjectModel;

namespace POriginsItemEditor.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private OtbData? _otbData;
    private DatData? _datData;
    private SprFile? _sprFile;
    private string? _otbPath;
    private List<ItemViewModel> _allItems = [];
    private List<ClientItemViewModel> _allClientItems = [];
    private readonly AppSettings _appSettings = AppSettings.Load();

    /// <summary>Original snapshots for reset: keyed by (Category, Id).</summary>
    private readonly Dictionary<(ThingCategory, ushort), DatThingType> _originalSnapshots = [];

    [ObservableProperty] private string _statusText = "Selecione a pasta do client para começar";
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ItemViewModel? _selectedItem;
    [ObservableProperty] private bool _showMismatchesOnly;
    [ObservableProperty] private int _totalItems;
    [ObservableProperty] private int _mismatchCount;
    [ObservableProperty] private int _filteredCount;
    [ObservableProperty] private bool _hasUnsavedChanges;
    [ObservableProperty] private string? _clientFolderPath;
    [ObservableProperty] private bool _isClientLoaded;
    [ObservableProperty] private bool _showDeprecatedOnly;

    // ── OTB item editing (double-click to open) ──
    [ObservableProperty] private bool _isOtbItemEditing;

    // ── OTB panel (right column) ──
    [ObservableProperty] private string _otbPanelSearchText = string.Empty;
    [ObservableProperty] private int _otbPanelCurrentPage = 1;
    [ObservableProperty] private int _otbPanelTotalPages = 1;
    private const int OtbPanelItemsPerPage = 100;
    private List<ItemViewModel> _otbPanelFilteredItems = [];
    public ObservableCollection<ItemViewModel> OtbPanelItems { get; } = [];
    private bool _suppressOtbClientSync;

    // ── Animation timer for client item list ──
    // Object Builder durations: Items=500ms, Outfits=300ms, Effects=100ms, Missiles=100ms
    private DispatcherTimer? _animationTimer;
    private int _animTickCounter;

    private void StartAnimationTimer()
    {
        if (_animationTimer != null) return;
        _animTickCounter = 0;
        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _animationTimer.Tick += OnAnimationTick;
        _animationTimer.Start();
    }

    private void StopAnimationTimer()
    {
        if (_animationTimer == null) return;
        _animationTimer.Stop();
        _animationTimer.Tick -= OnAnimationTick;
        _animationTimer = null;
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        if (_sprFile == null) return;
        _animTickCounter++;
        foreach (var item in ClientItems)
        {
            if (item.Frames <= 1) continue;
            // Category-based tick divisors: effects/missiles every tick (100ms),
            // outfits every 3 ticks (300ms), items every 5 ticks (500ms)
            int divisor = item.Category switch
            {
                ThingCategory.Effect => 1,
                ThingCategory.Missile => 1,
                ThingCategory.Outfit => 3,
                _ => 5
            };
            if (_animTickCounter % divisor != 0) continue;
            item.AnimFrame = (item.AnimFrame + 1) % item.Frames;
            item.Sprite = ComposeThingBitmap(item.ThingType, item.AnimFrame);
        }
    }

    // ── Right-side sprite panel (always visible when client loaded) ──
    [ObservableProperty] private int _rightSpriteCurrentPage = 1;
    [ObservableProperty] private int _rightSpriteTotalPages = 1;
    private const int RightSpritesPerPage = 100;
    public ObservableCollection<SpriteViewModel> RightSprites { get; } = [];

    // ── Client items (DAT view) ──
    [ObservableProperty] private string _clientSearchText = string.Empty;
    [ObservableProperty] private ClientItemViewModel? _selectedClientItem;
    [ObservableProperty] private bool _isClientItemEditing;
    [ObservableProperty] private int _clientFilteredCount;
    [ObservableProperty] private string _clientCategoryFilter = "All";
    [ObservableProperty] private string _clientNavigateId = string.Empty;
    [ObservableProperty] private int _clientCurrentPage = 1;
    [ObservableProperty] private int _clientTotalPages = 1;
    private const int ClientItemsPerPage = 100;
    private List<ClientItemViewModel> _clientFilteredItems = [];
    public string[] ClientCategoryOptions { get; } = ["All", "Item", "Outfit", "Effect", "Missile"];
    public ObservableCollection<ClientItemViewModel> ClientItems { get; } = [];

    // ── Client item view options ──
    [ObservableProperty] private bool _showCropSize;
    [ObservableProperty] private bool _showGrid;
    [ObservableProperty] private bool _showAllFrames;
    [ObservableProperty] private bool _isPlayingAnimation;

    public ObservableCollection<SpriteViewModel> FilmstripFrames { get; } = [];
    public bool HasAnimation => (CurrentFrameGroup?.Frames ?? 1) > 1;
    public bool HasMultipleGroups => (_currentCompositionThing?.FrameGroups.Length ?? 1) > 1;
    public bool HasMultipleLayers => (CurrentFrameGroup?.Layers ?? 1) > 1;
    public int AnimationFrameMax => Math.Max(0, (CurrentFrameGroup?.Frames ?? 1) - 1);
    public string AnimationLoopLabel
    {
        get
        {
            var fg = CurrentFrameGroup;
            if (fg == null || fg.Frames <= 1) return "";
            return fg.LoopCount switch
            {
                -1 => "Pingpong",
                0 => "Loop",
                _ => $"Loop ×{fg.LoopCount}"
            };
        }
    }

    partial void OnShowAllFramesChanged(bool value) => BuildFilmstrip();

    partial void OnIsPlayingAnimationChanged(bool value)
    {
        if (value) StartCompositionAnimTimer();
        else StopCompositionAnimTimer();
    }

    // ── Composition grid layout ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompositionPreviewWidth))]
    [NotifyPropertyChangedFor(nameof(CropOverlaySize))]
    private int _compositionGridColumns = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompositionPreviewHeight))]
    [NotifyPropertyChangedFor(nameof(CropOverlaySize))]
    private int _compositionGridRows = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CropOverlaySize))]
    private int _compositionExactSize = 32;

    [ObservableProperty] private SpriteViewModel? _selectedRightSprite;

    private const double CompositionCellSize = 64.0;
    private const double CompositionCellGap = 2.0;

    public double CompositionPreviewWidth => CompositionGridColumns * (CompositionCellSize + CompositionCellGap);
    public double CompositionPreviewHeight => CompositionGridRows * (CompositionCellSize + CompositionCellGap);
    public double CropOverlaySize => Math.Min(CompositionExactSize * 2.0, Math.Min(CompositionPreviewWidth, CompositionPreviewHeight));

    private static readonly ISolidColorBrush GridVisibleBrush = new SolidColorBrush(Color.Parse("#45475a"));
    private static readonly ISolidColorBrush GridHiddenBrush = new SolidColorBrush(Color.Parse("#11111b"));
    public ISolidColorBrush CompositionGridBrush => ShowGrid ? GridVisibleBrush : GridHiddenBrush;

    partial void OnShowGridChanged(bool value) => OnPropertyChanged(nameof(CompositionGridBrush));

    // ── Composition animation timer (plays animation in preview) ──
    private DispatcherTimer? _compositionAnimTimer;
    private int _compositionAnimDirection = 1;

    private void StartCompositionAnimTimer()
    {
        StopCompositionAnimTimer();
        var fg = CurrentFrameGroup;
        if (fg == null || fg.Frames <= 1) { IsPlayingAnimation = false; return; }

        _compositionAnimDirection = 1;
        int interval = GetFrameDurationMs(fg, 0);
        _compositionAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(interval) };
        _compositionAnimTimer.Tick += OnCompositionAnimTick;
        _compositionAnimTimer.Start();
    }

    private void StopCompositionAnimTimer()
    {
        if (_compositionAnimTimer == null) return;
        _compositionAnimTimer.Stop();
        _compositionAnimTimer.Tick -= OnCompositionAnimTick;
        _compositionAnimTimer = null;
    }

    private void OnCompositionAnimTick(object? sender, EventArgs e)
    {
        var fg = CurrentFrameGroup;
        if (fg == null || fg.Frames <= 1) { StopCompositionAnimTimer(); IsPlayingAnimation = false; return; }

        int nextFrame = CompositionFrame + _compositionAnimDirection;

        if (fg.LoopCount == -1) // Pingpong
        {
            if (nextFrame >= fg.Frames) { _compositionAnimDirection = -1; nextFrame = fg.Frames - 2; }
            if (nextFrame < 0) { _compositionAnimDirection = 1; nextFrame = 1; }
            nextFrame = Math.Clamp(nextFrame, 0, fg.Frames - 1);
        }
        else // Normal loop
        {
            if (nextFrame >= fg.Frames) nextFrame = 0;
        }

        CompositionFrame = nextFrame;

        // Update interval for next frame's duration
        if (_compositionAnimTimer != null)
            _compositionAnimTimer.Interval = TimeSpan.FromMilliseconds(GetFrameDurationMs(fg, CompositionFrame));
    }

    private static int GetFrameDurationMs(FrameGroup fg, int frame)
    {
        if (fg.FrameDurations != null && frame >= 0 && frame < fg.FrameDurations.Length)
        {
            var dur = fg.FrameDurations[frame];
            int ms = (int)((dur.Minimum + dur.Maximum) / 2);
            return Math.Max(50, ms);
        }
        return 200;
    }

    [RelayCommand]
    private void PreviousAnimationFrame()
    {
        var fg = CurrentFrameGroup;
        if (fg == null) return;
        CompositionFrame = CompositionFrame > 0 ? CompositionFrame - 1 : fg.Frames - 1;
    }

    [RelayCommand]
    private void NextAnimationFrame()
    {
        var fg = CurrentFrameGroup;
        if (fg == null) return;
        CompositionFrame = CompositionFrame < fg.Frames - 1 ? CompositionFrame + 1 : 0;
    }

    private void BuildFilmstrip()
    {
        FilmstripFrames.Clear();
        if (!ShowAllFrames) return;

        var thing = _currentCompositionThing;
        if (thing == null || _sprFile == null) return;

        var fg = CurrentFrameGroup;
        if (fg == null || fg.Frames <= 1) return;

        int layer = Math.Clamp(CompositionLayer, 0, Math.Max(0, fg.Layers - 1));
        int px = Math.Clamp(CompositionPatternX, 0, Math.Max(0, fg.PatternX - 1));
        int py = Math.Clamp(CompositionPatternY, 0, Math.Max(0, fg.PatternY - 1));
        int pz = Math.Clamp(CompositionPatternZ, 0, Math.Max(0, fg.PatternZ - 1));

        for (int f = 0; f < fg.Frames; f++)
        {
            var bitmap = ComposeFramePreview(fg, f, layer, px, py, pz);
            FilmstripFrames.Add(new SpriteViewModel { SpriteId = (uint)f, Bitmap = bitmap });
        }
    }

    private WriteableBitmap? ComposeFramePreview(FrameGroup fg, int frame, int layer, int px, int py, int pz)
    {
        if (_sprFile == null) return null;
        int w = fg.Width, h = fg.Height;
        if (w == 0 || h == 0) return null;

        if (w == 1 && h == 1)
        {
            uint sprId = fg.GetSpriteId(0, 0, layer, px, py, pz, frame);
            return LoadSpriteBitmap(sprId);
        }

        int bmpW = w * 32, bmpH = h * 32;
        var pixels = new byte[bmpW * bmpH * 4];

        for (int tw = 0; tw < w; tw++)
        {
            for (int th = 0; th < h; th++)
            {
                uint sprId = fg.GetSpriteId(tw, th, layer, px, py, pz, frame);
                var rgba = _sprFile.GetSpriteRgba(sprId);
                if (rgba == null) continue;
                int destX = (w - 1 - tw) * 32;
                int destY = (h - 1 - th) * 32;
                for (int y = 0; y < 32; y++)
                {
                    for (int x = 0; x < 32; x++)
                    {
                        int srcIdx = (y * 32 + x) * 4;
                        byte a = rgba[srcIdx + 3];
                        if (a == 0) continue;
                        int dstIdx = ((destY + y) * bmpW + destX + x) * 4;
                        pixels[dstIdx] = rgba[srcIdx];
                        pixels[dstIdx + 1] = rgba[srcIdx + 1];
                        pixels[dstIdx + 2] = rgba[srcIdx + 2];
                        pixels[dstIdx + 3] = a;
                    }
                }
            }
        }

        try
        {
            var bitmap = new WriteableBitmap(
                new PixelSize(bmpW, bmpH), new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Unpremul);
            using (var fb = bitmap.Lock())
                Marshal.Copy(pixels, 0, fb.Address, pixels.Length);
            return bitmap;
        }
        catch { return null; }
    }

    // ── Sprite composition (for selected item/client item) ──
    [ObservableProperty] private int _compositionFrameGroupIndex;
    [ObservableProperty] private int _compositionFrame;
    [ObservableProperty] private int _compositionLayer;
    [ObservableProperty] private int _compositionPatternX;
    [ObservableProperty] private int _compositionPatternY;
    [ObservableProperty] private int _compositionPatternZ;
    public ObservableCollection<SpriteViewModel> CompositionSprites { get; } = [];

    // ── Category visibility helpers (for conditional controls like OB) ──
    public bool IsItemSelected => SelectedClientItem?.ThingType.Category == ThingCategory.Item;
    public bool IsOutfitSelected => SelectedClientItem?.ThingType.Category == ThingCategory.Outfit;
    public bool IsEffectSelected => SelectedClientItem?.ThingType.Category == ThingCategory.Effect;
    public bool IsMissileSelected => SelectedClientItem?.ThingType.Category == ThingCategory.Missile;
    public bool IsNotMissileSelected => SelectedClientItem != null && SelectedClientItem.ThingType.Category != ThingCategory.Missile;

    // ── OB-style navigation visibility ──
    // Outfits/missiles use per-pattern navigation; items/effects show all patterns at once
    public bool IsOutfitOrMissile => IsOutfitSelected || IsMissileSelected;
    public bool ShowPatternZNav => (CurrentFrameGroup?.PatternZ ?? 1) > 1;

    // Direction label for outfits (N/E/S/W based on PatternX index)
    public string DirectionLabel => CompositionPatternX switch
    {
        0 => "↑ N",
        1 => "→ E",
        2 => "↓ S",
        3 => "← W",
        _ => $"Dir {CompositionPatternX}"
    };

    // ── OB-style direction commands (set PatternX to specific direction) ──
    [RelayCommand] private void SetDirectionNorth() => CompositionPatternX = 0;
    [RelayCommand] private void SetDirectionEast() => CompositionPatternX = 1;
    [RelayCommand] private void SetDirectionSouth() => CompositionPatternX = 2;
    [RelayCommand] private void SetDirectionWest() => CompositionPatternX = 3;

    // ── Texture property bindings (read/write to current FrameGroup) ──
    public int CompositionFrameGroupCount => _currentCompositionThing?.FrameGroups.Length ?? 1;

    public int CompositionWidth
    {
        get => CurrentFrameGroup?.Width ?? 1;
        set { var fg = CurrentFrameGroup; if (fg != null && value >= 1 && value <= 8) { fg.Width = (byte)value; fg.UpdateSpriteCount(); OnPropertyChanged(); NotifyAllCompositionLabels(); ReloadComposition(); } }
    }
    public int CompositionHeight
    {
        get => CurrentFrameGroup?.Height ?? 1;
        set { var fg = CurrentFrameGroup; if (fg != null && value >= 1 && value <= 8) { fg.Height = (byte)value; fg.UpdateSpriteCount(); OnPropertyChanged(); NotifyAllCompositionLabels(); ReloadComposition(); } }
    }
    public int CompositionCropSize
    {
        get => CurrentFrameGroup?.ExactSize ?? 32;
        set { var fg = CurrentFrameGroup; if (fg != null && value >= 1 && value <= 64) { fg.ExactSize = (byte)value; OnPropertyChanged(); ReloadComposition(); } }
    }
    public int CompositionLayerCount
    {
        get => CurrentFrameGroup?.Layers ?? 1;
        set { var fg = CurrentFrameGroup; if (fg != null && value >= 1 && value <= 8) { fg.Layers = (byte)value; fg.UpdateSpriteCount(); ClampNavigationIndices(); OnPropertyChanged(); NotifyAllCompositionLabels(); ReloadComposition(); } }
    }
    public int CompositionPatternXCount
    {
        get => CurrentFrameGroup?.PatternX ?? 1;
        set { var fg = CurrentFrameGroup; if (fg != null && value >= 1 && value <= 8) { fg.PatternX = (byte)value; fg.UpdateSpriteCount(); ClampNavigationIndices(); OnPropertyChanged(); NotifyAllCompositionLabels(); ReloadComposition(); } }
    }
    public int CompositionPatternYCount
    {
        get => CurrentFrameGroup?.PatternY ?? 1;
        set { var fg = CurrentFrameGroup; if (fg != null && value >= 1 && value <= 8) { fg.PatternY = (byte)value; fg.UpdateSpriteCount(); ClampNavigationIndices(); OnPropertyChanged(); NotifyAllCompositionLabels(); ReloadComposition(); } }
    }
    public int CompositionPatternZCount
    {
        get => CurrentFrameGroup?.PatternZ ?? 1;
        set { var fg = CurrentFrameGroup; if (fg != null && value >= 1 && value <= 8) { fg.PatternZ = (byte)value; fg.UpdateSpriteCount(); ClampNavigationIndices(); OnPropertyChanged(); NotifyAllCompositionLabels(); ReloadComposition(); } }
    }
    public int CompositionFrameCount
    {
        get => CurrentFrameGroup?.Frames ?? 1;
        set { var fg = CurrentFrameGroup; if (fg != null && value >= 1 && value <= 255) { fg.Frames = (byte)value; fg.UpdateSpriteCount(); ClampNavigationIndices(); OnPropertyChanged(); NotifyAllCompositionLabels(); ReloadComposition(); } }
    }

    // ── Appearance labels ──
    public string CompositionFrameGroupName => CompositionFrameGroupIndex == 0 ? "Idle" : "Walking";
    public bool IsIdleGroup { get => CompositionFrameGroupIndex == 0; set { if (value) CompositionFrameGroupIndex = 0; } }
    public bool IsWalkingGroup { get => CompositionFrameGroupIndex == 1; set { if (value) CompositionFrameGroupIndex = 1; } }
    public string CompositionFrameLabel => $"{CompositionFrame}/{Math.Max(0, (CurrentFrameGroup?.Frames ?? 1) - 1)}";
    public string CompositionLayerLabel => $"{CompositionLayer}/{Math.Max(0, (CurrentFrameGroup?.Layers ?? 1) - 1)}";
    public string CompositionPatternXLabel => $"{CompositionPatternX}/{Math.Max(0, (CurrentFrameGroup?.PatternX ?? 1) - 1)}";
    public string CompositionPatternYLabel => $"{CompositionPatternY}/{Math.Max(0, (CurrentFrameGroup?.PatternY ?? 1) - 1)}";

    private FrameGroup? CurrentFrameGroup
    {
        get
        {
            var thing = _currentCompositionThing;
            if (thing == null || thing.FrameGroups.Length == 0) return null;
            int idx = Math.Clamp(CompositionFrameGroupIndex, 0, thing.FrameGroups.Length - 1);
            return thing.FrameGroups[idx];
        }
    }

    /// <summary>
    /// Clamps navigation indices to valid range after a structural property changes
    /// (e.g. reducing PatternX from 4 to 2 when CompositionPatternX was 3).
    /// </summary>
    private void ClampNavigationIndices()
    {
        var fg = CurrentFrameGroup;
        if (fg == null) return;
        if (CompositionFrame >= fg.Frames) CompositionFrame = fg.Frames - 1;
        if (CompositionLayer >= fg.Layers) CompositionLayer = fg.Layers - 1;
        if (CompositionPatternX >= fg.PatternX) CompositionPatternX = fg.PatternX - 1;
        if (CompositionPatternY >= fg.PatternY) CompositionPatternY = fg.PatternY - 1;
        if (CompositionPatternZ >= fg.PatternZ) CompositionPatternZ = fg.PatternZ - 1;
    }

    private void NotifyAllCompositionLabels()
    {
        OnPropertyChanged(nameof(CompositionFrameGroupCount));
        OnPropertyChanged(nameof(CompositionWidth));
        OnPropertyChanged(nameof(CompositionHeight));
        OnPropertyChanged(nameof(CompositionCropSize));
        OnPropertyChanged(nameof(CompositionLayerCount));
        OnPropertyChanged(nameof(CompositionPatternXCount));
        OnPropertyChanged(nameof(CompositionPatternYCount));
        OnPropertyChanged(nameof(CompositionPatternZCount));
        OnPropertyChanged(nameof(CompositionFrameCount));
        OnPropertyChanged(nameof(CompositionFrameGroupName));
        OnPropertyChanged(nameof(IsIdleGroup));
        OnPropertyChanged(nameof(IsWalkingGroup));
        OnPropertyChanged(nameof(CompositionFrameLabel));
        OnPropertyChanged(nameof(CompositionLayerLabel));
        OnPropertyChanged(nameof(CompositionPatternXLabel));
        OnPropertyChanged(nameof(CompositionPatternYLabel));
        OnPropertyChanged(nameof(HasAnimation));
        OnPropertyChanged(nameof(HasMultipleGroups));
        OnPropertyChanged(nameof(HasMultipleLayers));
        OnPropertyChanged(nameof(AnimationFrameMax));
        OnPropertyChanged(nameof(AnimationLoopLabel));
        OnPropertyChanged(nameof(IsOutfitOrMissile));
        OnPropertyChanged(nameof(ShowPatternZNav));
        OnPropertyChanged(nameof(DirectionLabel));
    }

    // ── Pattern spin commands (modify FrameGroup values) ──
    [RelayCommand] private void IncrementFrameGroupCount() { } // TODO: structural change — adding/removing frame groups
    [RelayCommand] private void DecrementFrameGroupCount() { }
    [RelayCommand] private void IncrementWidth() { if (CompositionWidth < 8) CompositionWidth++; }
    [RelayCommand] private void DecrementWidth() { if (CompositionWidth > 1) CompositionWidth--; }
    [RelayCommand] private void IncrementHeight() { if (CompositionHeight < 8) CompositionHeight++; }
    [RelayCommand] private void DecrementHeight() { if (CompositionHeight > 1) CompositionHeight--; }
    [RelayCommand] private void IncrementCropSize() { if (CompositionCropSize < 64) CompositionCropSize++; }
    [RelayCommand] private void DecrementCropSize() { if (CompositionCropSize > 1) CompositionCropSize--; }
    [RelayCommand] private void IncrementLayerCount() { if (CompositionLayerCount < 8) CompositionLayerCount++; }
    [RelayCommand] private void DecrementLayerCount() { if (CompositionLayerCount > 1) CompositionLayerCount--; }
    [RelayCommand] private void IncrementPatternXCount() { if (CompositionPatternXCount < 8) CompositionPatternXCount++; }
    [RelayCommand] private void DecrementPatternXCount() { if (CompositionPatternXCount > 1) CompositionPatternXCount--; }
    [RelayCommand] private void IncrementPatternYCount() { if (CompositionPatternYCount < 8) CompositionPatternYCount++; }
    [RelayCommand] private void DecrementPatternYCount() { if (CompositionPatternYCount > 1) CompositionPatternYCount--; }
    [RelayCommand] private void IncrementPatternZCount() { if (CompositionPatternZCount < 8) CompositionPatternZCount++; }
    [RelayCommand] private void DecrementPatternZCount() { if (CompositionPatternZCount > 1) CompositionPatternZCount--; }
    [RelayCommand] private void IncrementFrameCount() { if (CompositionFrameCount < 255) CompositionFrameCount++; }
    [RelayCommand] private void DecrementFrameCount() { if (CompositionFrameCount > 1) CompositionFrameCount--; }

    // ── Appearance spin commands ──
    [RelayCommand]
    private void IncrementFrameGroupIndex()
    {
        var thing = _currentCompositionThing;
        if (thing != null && CompositionFrameGroupIndex < thing.FrameGroups.Length - 1)
            CompositionFrameGroupIndex++;
    }
    [RelayCommand]
    private void DecrementFrameGroupIndex()
    {
        if (CompositionFrameGroupIndex > 0) CompositionFrameGroupIndex--;
    }
    [RelayCommand]
    private void IncrementFrame()
    {
        var fg = CurrentFrameGroup;
        if (fg != null && CompositionFrame < fg.Frames - 1) CompositionFrame++;
    }
    [RelayCommand]
    private void DecrementFrame()
    {
        if (CompositionFrame > 0) CompositionFrame--;
    }
    [RelayCommand]
    private void IncrementLayer()
    {
        var fg = CurrentFrameGroup;
        if (fg != null && CompositionLayer < fg.Layers - 1) CompositionLayer++;
    }
    [RelayCommand]
    private void DecrementLayer()
    {
        if (CompositionLayer > 0) CompositionLayer--;
    }
    [RelayCommand]
    private void IncrementPatternX()
    {
        var fg = CurrentFrameGroup;
        if (fg != null && CompositionPatternX < fg.PatternX - 1) CompositionPatternX++;
    }
    [RelayCommand]
    private void DecrementPatternX()
    {
        if (CompositionPatternX > 0) CompositionPatternX--;
    }
    [RelayCommand]
    private void IncrementPatternY()
    {
        var fg = CurrentFrameGroup;
        if (fg != null && CompositionPatternY < fg.PatternY - 1) CompositionPatternY++;
    }
    [RelayCommand]
    private void DecrementPatternY()
    {
        if (CompositionPatternY > 0) CompositionPatternY--;
    }
    [RelayCommand]
    private void IncrementPatternZ()
    {
        var fg = CurrentFrameGroup;
        if (fg != null && CompositionPatternZ < fg.PatternZ - 1) CompositionPatternZ++;
    }
    [RelayCommand]
    private void DecrementPatternZ()
    {
        if (CompositionPatternZ > 0) CompositionPatternZ--;
    }

    // ── Dropdown options for ComboBoxes ──
    public OtbGroup[] GroupOptions { get; } = [OtbGroup.None, OtbGroup.Ground, OtbGroup.Container, OtbGroup.Splash, OtbGroup.Fluid, OtbGroup.Deprecated];
    public string[] StackOrderOptions { get; } = ["None", "Border", "Bottom", "Top"];
    public MinimapColorEntry[] MinimapPalette => TibiaColors.Palette;

    public ObservableCollection<ItemViewModel> Items { get; } = [];

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnShowMismatchesOnlyChanged(bool value) => ApplyFilter();
    partial void OnShowDeprecatedOnlyChanged(bool value) => ApplyFilter();
    partial void OnClientSearchTextChanged(string value) { ClientCurrentPage = 1; ApplyClientFilter(); }
    partial void OnClientCategoryFilterChanged(string value) { ClientCurrentPage = 1; ApplyClientFilter(); }
    partial void OnOtbPanelSearchTextChanged(string value) { OtbPanelCurrentPage = 1; ApplyOtbPanelFilter(); }

    partial void OnSelectedItemChanged(ItemViewModel? value)
    {
        // Link OTB → Client: auto-select matching client item
        if (value != null && !_suppressOtbClientSync)
        {
            _suppressOtbClientSync = true;
            SelectClientItemByClientId(value.ClientId);
            _suppressOtbClientSync = false;
        }
    }

    public void OpenOtbItemEditor()
    {
        if (SelectedItem == null) return;
        if (SelectedItem.DatThingType != null)
            LoadComposition(SelectedItem.DatThingType);
        IsOtbItemEditing = true;
    }

    partial void OnCompositionFrameChanged(int value) { OnPropertyChanged(nameof(CompositionFrameLabel)); ReloadCompositionGridOnly(); }
    partial void OnCompositionLayerChanged(int value) { OnPropertyChanged(nameof(CompositionLayerLabel)); ReloadComposition(); }
    partial void OnCompositionPatternXChanged(int value) { OnPropertyChanged(nameof(CompositionPatternXLabel)); OnPropertyChanged(nameof(DirectionLabel)); ReloadComposition(); }
    partial void OnCompositionPatternYChanged(int value) { OnPropertyChanged(nameof(CompositionPatternYLabel)); ReloadComposition(); }
    partial void OnCompositionPatternZChanged(int value) => ReloadComposition();
    partial void OnCompositionFrameGroupIndexChanged(int value) { NotifyAllCompositionLabels(); ReloadComposition(); }

    [RelayCommand]
    private async Task SelectClientFolderAsync()
    {
        var folderPath = await FileDialogHelper.OpenFolderAsync("Selecionar pasta do Client");
        if (folderPath == null) return;
        await LoadClientFromFolder(folderPath);
    }

    private async Task LoadClientFromFolder(string folderPath)
    {
        // Search for .dat and .spr files
        var (datPath, sprPath) = FindClientFiles(folderPath);
        if (datPath == null || sprPath == null)
        {
            StatusText = "Não encontrado Tibia.dat/Tibia.spr na pasta selecionada";
            return;
        }

        try
        {
            _sprFile?.Dispose();
            _datData = DatFile.Load(datPath);
            _sprFile = SprFile.Load(sprPath);
            ClientFolderPath = folderPath;
            IsClientLoaded = true;
            StatusText = $"Client carregado: {_datData.ItemCount} itens, {_sprFile.SpriteCount} sprites — {Path.GetFileName(Path.GetDirectoryName(datPath))}";

            _appSettings.LastClientFolderPath = folderPath;
            _appSettings.Save();

            // Init client items list and right sprite panel
            InitRightSpriteList();
            BuildClientItemList();

            // If OTB already loaded, cross-reference and load sprites
            if (_otbData != null)
            {
                CrossReferenceDat();
                LoadAllSprites();
                ApplyFilter();
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Erro ao carregar client: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenOtbAsync()
    {
        var path = await FileDialogHelper.OpenFileAsync("Abrir items.otb", [("OTB Files", "*.otb"), ("All Files", "*")]);
        if (path == null) return;
        await LoadOtbFromPath(path);
    }

    private async Task LoadOtbFromPath(string path)
    {
        try
        {
            _otbData = OtbFile.Load(path);
            _otbPath = path;
            BuildItemList();
            StatusText = $"OTB carregado: {_otbData.Items.Count} itens — {Path.GetFileName(path)}";
            HasUnsavedChanges = false;

            _appSettings.LastOtbPath = path;
            _appSettings.Save();
        }
        catch (Exception ex)
        {
            StatusText = $"Erro ao carregar OTB: {ex.Message}";
        }
    }

    public async Task TryLoadLastSessionAsync()
    {
        if (!string.IsNullOrEmpty(_appSettings.LastClientFolderPath) && Directory.Exists(_appSettings.LastClientFolderPath))
            await LoadClientFromFolder(_appSettings.LastClientFolderPath);
        if (!string.IsNullOrEmpty(_appSettings.LastOtbPath) && File.Exists(_appSettings.LastOtbPath))
            await LoadOtbFromPath(_appSettings.LastOtbPath);
    }

    [RelayCommand]
    private async Task SaveOtbAsync()
    {
        if (_otbData == null || _otbPath == null) return;

        try
        {
            foreach (var vm in _allItems)
                vm.ApplyToModel();

            OtbFile.Save(_otbPath, _otbData);
            HasUnsavedChanges = false;
            StatusText = $"Salvo: {Path.GetFileName(_otbPath)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Erro ao salvar: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveOtbAsAsync()
    {
        if (_otbData == null) return;

        var path = await FileDialogHelper.SaveFileAsync("Salvar items.otb como...", [("OTB Files", "*.otb")]);
        if (path == null) return;

        try
        {
            foreach (var vm in _allItems)
                vm.ApplyToModel();

            OtbFile.Save(path, _otbData);
            _otbPath = path;
            HasUnsavedChanges = false;
            StatusText = $"Salvo como: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Erro ao salvar: {ex.Message}";
        }
    }

    [RelayCommand]
    private void FixAllMismatches()
    {
        int fixed_ = 0;
        foreach (var vm in _allItems)
        {
            if (vm.HasMismatch && vm.DatAnimPhases > 1 && !vm.IsAnimation)
            {
                vm.IsAnimation = true;
                fixed_++;
            }
            else if (vm.HasMismatch && vm.DatAnimPhases <= 1 && vm.IsAnimation)
            {
                vm.IsAnimation = false;
                fixed_++;
            }
        }

        if (fixed_ > 0)
        {
            HasUnsavedChanges = true;
            CrossReferenceDat();
            StatusText = $"{fixed_} mismatches corrigidos automaticamente";
        }
    }

    public void MarkDirty() => HasUnsavedChanges = true;

    private void BuildItemList()
    {
        _allItems.Clear();
        Items.Clear();

        if (_otbData == null) return;

        foreach (var item in _otbData.Items)
        {
            var vm = new ItemViewModel(item, this);
            _allItems.Add(vm);
        }

        TotalItems = _allItems.Count;
        CrossReferenceDat();
        LoadAllSprites();
        ApplyFilter();
    }

    private void CrossReferenceDat()
    {
        int mismatches = 0;
        foreach (var vm in _allItems)
        {
            if (_datData != null && _datData.Items.TryGetValue(vm.ClientId, out var datThing))
            {
                // Sum animation phases across all frame groups
                int totalPhases = 0;
                foreach (var fg in datThing.FrameGroups)
                    totalPhases += fg.Frames;

                vm.DatAnimPhases = totalPhases;
                vm.DatAnimateAlways = datThing.IsAnimateAlways;
                vm.FirstSpriteId = datThing.FirstSpriteId;
                vm.DatThingType = datThing;
                vm.HasMismatch = (totalPhases > 1) != vm.IsAnimation;
            }
            else
            {
                vm.DatAnimPhases = 0;
                vm.DatAnimateAlways = false;
                vm.FirstSpriteId = 0;
                vm.DatThingType = null;
                vm.HasMismatch = false;
            }

            if (vm.HasMismatch) mismatches++;
        }
        MismatchCount = mismatches;
    }

    private void LoadAllSprites()
    {
        if (_sprFile == null) return;

        foreach (var vm in _allItems)
        {
            if (vm.FirstSpriteId == 0)
            {
                vm.Sprite = null;
                continue;
            }
            vm.Sprite = LoadSpriteBitmap(vm.FirstSpriteId);
        }
    }

    private static (string? datPath, string? sprPath) FindClientFiles(string folder)
    {
        // Search in the folder and common subfolders
        string[] searchPaths = [
            folder,
            Path.Combine(folder, "data", "things"),
        ];

        foreach (var basePath in searchPaths)
        {
            if (!Directory.Exists(basePath)) continue;

            // Check directly
            var dat = Path.Combine(basePath, "Tibia.dat");
            var spr = Path.Combine(basePath, "Tibia.spr");
            if (File.Exists(dat) && File.Exists(spr))
                return (dat, spr);

            // Check subdirectories (e.g., 1098/)
            foreach (var subDir in Directory.GetDirectories(basePath))
            {
                dat = Path.Combine(subDir, "Tibia.dat");
                spr = Path.Combine(subDir, "Tibia.spr");
                if (File.Exists(dat) && File.Exists(spr))
                    return (dat, spr);
            }
        }

        return (null, null);
    }

    private void ApplyFilter()
    {
        Items.Clear();

        var search = SearchText.Trim();
        bool hasSearch = !string.IsNullOrEmpty(search);
        ushort numericId = 0;
        bool isNumericSearch = hasSearch && ushort.TryParse(search, out numericId);

        foreach (var vm in _allItems)
        {
            // When "Só Deprecated" is on, show only deprecated items
            // By default hide deprecated unless the filter is active
            if (ShowDeprecatedOnly && !vm.IsDeprecated) continue;
            if (!ShowDeprecatedOnly && vm.IsDeprecated) continue;
            if (ShowMismatchesOnly && !vm.HasMismatch) continue;

            if (hasSearch)
            {
                if (isNumericSearch)
                {
                    if (vm.ServerId != numericId && vm.ClientId != numericId) continue;
                }
                else
                {
                    if (!vm.GroupName.Contains(search, StringComparison.OrdinalIgnoreCase)
                        && !vm.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
            }

            Items.Add(vm);
        }

        FilteredCount = Items.Count;
        ApplyOtbPanelFilter();
    }

    // ══════════════════════════════════════════════════════════════════════
    // ── OTB Panel (right column) ──
    // ══════════════════════════════════════════════════════════════════════

    private void ApplyOtbPanelFilter()
    {
        _otbPanelFilteredItems.Clear();
        OtbPanelItems.Clear();

        var search = OtbPanelSearchText.Trim();
        bool hasSearch = !string.IsNullOrEmpty(search);
        ushort numericId = 0;
        bool isNumericSearch = hasSearch && ushort.TryParse(search, out numericId);

        foreach (var vm in _allItems)
        {
            if (vm.IsDeprecated) continue;
            if (hasSearch)
            {
                if (isNumericSearch)
                {
                    if (vm.ServerId != numericId && vm.ClientId != numericId) continue;
                }
                else
                {
                    if (!vm.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                        && !vm.GroupName.Contains(search, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
            }
            _otbPanelFilteredItems.Add(vm);
        }

        OtbPanelTotalPages = Math.Max(1, (_otbPanelFilteredItems.Count + OtbPanelItemsPerPage - 1) / OtbPanelItemsPerPage);
        if (OtbPanelCurrentPage > OtbPanelTotalPages) OtbPanelCurrentPage = OtbPanelTotalPages;
        LoadOtbPanelPage();
    }

    private void LoadOtbPanelPage()
    {
        OtbPanelItems.Clear();
        int start = (OtbPanelCurrentPage - 1) * OtbPanelItemsPerPage;
        int end = Math.Min(start + OtbPanelItemsPerPage, _otbPanelFilteredItems.Count);
        for (int i = start; i < end; i++)
            OtbPanelItems.Add(_otbPanelFilteredItems[i]);
    }

    [RelayCommand] private void OtbPanelFirstPage() { OtbPanelCurrentPage = 1; LoadOtbPanelPage(); }
    [RelayCommand] private void OtbPanelPrevPage() { if (OtbPanelCurrentPage > 1) { OtbPanelCurrentPage--; LoadOtbPanelPage(); } }
    [RelayCommand] private void OtbPanelNextPage() { if (OtbPanelCurrentPage < OtbPanelTotalPages) { OtbPanelCurrentPage++; LoadOtbPanelPage(); } }
    [RelayCommand] private void OtbPanelLastPage() { OtbPanelCurrentPage = OtbPanelTotalPages; LoadOtbPanelPage(); }

    /// <summary>Find and select the OTB item matching a clientId, navigating to the correct page.</summary>
    private void SelectOtbItemByClientId(ushort clientId)
    {
        int idx = _otbPanelFilteredItems.FindIndex(v => v.ClientId == clientId);
        if (idx < 0) return;
        int page = idx / OtbPanelItemsPerPage + 1;
        if (page != OtbPanelCurrentPage)
        {
            OtbPanelCurrentPage = page;
            LoadOtbPanelPage();
        }
        SelectedItem = _otbPanelFilteredItems[idx];
    }

    /// <summary>Find and select the client item matching a clientId, navigating to the correct page.</summary>
    private void SelectClientItemByClientId(ushort clientId)
    {
        var cat = ClientCategoryFilter;
        var source = cat == "All" ? _allClientItems :
            _allClientItems.Where(c => c.CategoryName.Equals(cat, StringComparison.OrdinalIgnoreCase)).ToList();
        int idx = source.FindIndex(c => c.Id == clientId);
        if (idx < 0) return;
        int page = idx / ClientItemsPerPage + 1;
        if (page != ClientCurrentPage)
        {
            ClientCurrentPage = page;
            // Rebuild filtered items for the page
            _clientFilteredItems = source;
            ClientTotalPages = Math.Max(1, (_clientFilteredItems.Count + ClientItemsPerPage - 1) / ClientItemsPerPage);
            LoadClientPage();
        }
        SelectedClientItem = source[idx];
    }

    // ══════════════════════════════════════════════════════════════════════
    // ── Client Items (DAT view) ──
    // ══════════════════════════════════════════════════════════════════════

    private void BuildClientItemList()
    {
        _allClientItems.Clear();
        ClientItems.Clear();
        if (_datData == null) return;

        void AddCategory(Dictionary<ushort, DatThingType> dict)
        {
            foreach (var kvp in dict.OrderBy(x => x.Key))
            {
                var vm = new ClientItemViewModel(kvp.Value);
                vm.Sprite = ComposeThingBitmap(kvp.Value);
                _allClientItems.Add(vm);
            }
        }

        AddCategory(_datData.Items);
        AddCategory(_datData.Outfits);
        AddCategory(_datData.Effects);
        AddCategory(_datData.Missiles);

        ApplyClientFilter();
        StartAnimationTimer();
    }

    private void ApplyClientFilter()
    {
        _clientFilteredItems.Clear();
        ClientItems.Clear();

        var search = ClientSearchText.Trim();
        bool hasSearch = !string.IsNullOrEmpty(search);
        ushort numericId = 0;
        bool isNumericSearch = hasSearch && ushort.TryParse(search, out numericId);

        foreach (var vm in _allClientItems)
        {
            if (ClientCategoryFilter != "All")
            {
                if (!vm.CategoryName.Equals(ClientCategoryFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            if (hasSearch)
            {
                if (isNumericSearch)
                {
                    if (vm.Id != numericId) continue;
                }
                else
                {
                    if (!vm.CategoryName.Contains(search, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
            }

            _clientFilteredItems.Add(vm);
        }

        ClientFilteredCount = _clientFilteredItems.Count;
        ClientTotalPages = Math.Max(1, (_clientFilteredItems.Count + ClientItemsPerPage - 1) / ClientItemsPerPage);
        ClientCurrentPage = Math.Clamp(ClientCurrentPage, 1, ClientTotalPages);
        LoadClientPage();
    }

    private void LoadClientPage()
    {
        ClientItems.Clear();
        int start = (ClientCurrentPage - 1) * ClientItemsPerPage;
        int end = Math.Min(start + ClientItemsPerPage, _clientFilteredItems.Count);
        for (int i = start; i < end; i++)
            ClientItems.Add(_clientFilteredItems[i]);

        if (ClientItems.Count > 0 && (SelectedClientItem == null || !ClientItems.Contains(SelectedClientItem)))
            SelectedClientItem = ClientItems[0];
    }

    partial void OnSelectedClientItemChanged(ClientItemViewModel? value)
    {
        if (value != null)
        {
            _suppressNavigateSync = true;
            ClientNavigateId = value.Id.ToString();
            _suppressNavigateSync = false;

            // Link Client → OTB: auto-select matching OTB item
            if (!_suppressOtbClientSync)
            {
                _suppressOtbClientSync = true;
                SelectOtbItemByClientId(value.Id);
                _suppressOtbClientSync = false;
            }
        }
        else
        {
            IsClientItemEditing = false;
            CompositionSprites.Clear();
        }

        OnPropertyChanged(nameof(IsItemSelected));
        OnPropertyChanged(nameof(IsOutfitSelected));
        OnPropertyChanged(nameof(IsEffectSelected));
        OnPropertyChanged(nameof(IsMissileSelected));
        OnPropertyChanged(nameof(IsNotMissileSelected));
    }

    public void OpenClientItemEditor()
    {
        if (SelectedClientItem == null) return;
        LoadComposition(SelectedClientItem.ThingType);
        IsClientItemEditing = true;
        // Also show the linked OTB detail panel if an OTB item is linked
        IsOtbItemEditing = SelectedItem != null;
    }

    private bool _suppressNavigateSync;

    partial void OnClientNavigateIdChanged(string value)
    {
        if (_suppressNavigateSync) return;
        if (ushort.TryParse(value.Trim(), out var targetId))
        {
            // Find in filtered items and navigate to correct page
            int idx = _clientFilteredItems.FindIndex(c => c.Id == targetId);
            if (idx >= 0)
            {
                int page = idx / ClientItemsPerPage + 1;
                if (page != ClientCurrentPage)
                {
                    ClientCurrentPage = page;
                    LoadClientPage();
                }
                SelectedClientItem = _clientFilteredItems[idx];
            }
        }
    }

    // ── Client item page navigation ──

    [RelayCommand]
    private void ClientFirstPage()
    {
        ClientCurrentPage = 1;
        LoadClientPage();
    }

    [RelayCommand]
    private void ClientPrevPage()
    {
        if (ClientCurrentPage > 1) { ClientCurrentPage--; LoadClientPage(); }
    }

    [RelayCommand]
    private void ClientNextPage()
    {
        if (ClientCurrentPage < ClientTotalPages) { ClientCurrentPage++; LoadClientPage(); }
    }

    [RelayCommand]
    private void ClientLastPage()
    {
        ClientCurrentPage = ClientTotalPages;
        LoadClientPage();
    }

    [RelayCommand]
    private void ClientPrevItem()
    {
        if (SelectedClientItem == null) return;
        int idx = _clientFilteredItems.IndexOf(SelectedClientItem);
        if (idx <= 0) return;
        idx--;
        int page = idx / ClientItemsPerPage + 1;
        if (page != ClientCurrentPage)
        {
            ClientCurrentPage = page;
            LoadClientPage();
        }
        SelectedClientItem = _clientFilteredItems[idx];
    }

    [RelayCommand]
    private void ClientNextItem()
    {
        if (SelectedClientItem == null) return;
        int idx = _clientFilteredItems.IndexOf(SelectedClientItem);
        if (idx < 0 || idx >= _clientFilteredItems.Count - 1) return;
        idx++;
        int page = idx / ClientItemsPerPage + 1;
        if (page != ClientCurrentPage)
        {
            ClientCurrentPage = page;
            LoadClientPage();
        }
        SelectedClientItem = _clientFilteredItems[idx];
    }

    // ══════════════════════════════════════════════════════════════════════
    // ── Right-side Sprite Panel ──
    // ══════════════════════════════════════════════════════════════════════

    [RelayCommand]
    private void RightSpriteFirstPage()
    {
        RightSpriteCurrentPage = 1;
        LoadRightSpritePage();
    }

    [RelayCommand]
    private void RightSpritePrevPage()
    {
        if (RightSpriteCurrentPage > 1) { RightSpriteCurrentPage--; LoadRightSpritePage(); }
    }

    [RelayCommand]
    private void RightSpriteNextPage()
    {
        if (RightSpriteCurrentPage < RightSpriteTotalPages) { RightSpriteCurrentPage++; LoadRightSpritePage(); }
    }

    [RelayCommand]
    private void RightSpriteLastPage()
    {
        RightSpriteCurrentPage = RightSpriteTotalPages;
        LoadRightSpritePage();
    }

    private void InitRightSpriteList()
    {
        if (_sprFile == null) return;
        RightSpriteTotalPages = Math.Max(1, ((int)_sprFile.SpriteCount + RightSpritesPerPage - 1) / RightSpritesPerPage);
        RightSpriteCurrentPage = 1;
        LoadRightSpritePage();
    }

    private void LoadRightSpritePage()
    {
        RightSprites.Clear();
        if (_sprFile == null) return;

        int total = (int)_sprFile.SpriteCount;
        int startId = (RightSpriteCurrentPage - 1) * RightSpritesPerPage + 1;
        int endId = Math.Min(startId + RightSpritesPerPage - 1, total);

        for (int id = startId; id <= endId; id++)
        {
            var vm = new SpriteViewModel { SpriteId = (uint)id };
            vm.Bitmap = LoadSpriteBitmap((uint)id);
            RightSprites.Add(vm);
        }
    }

    private DatThingType? _currentCompositionThing;

    private void LoadComposition(DatThingType thing)
    {
        _currentCompositionThing = thing;

        // Snapshot original state for reset (only if not already stored)
        var key = (thing.Category, thing.Id);
        if (!_originalSnapshots.ContainsKey(key))
            _originalSnapshots[key] = thing.Clone();

        StopCompositionAnimTimer();
        IsPlayingAnimation = false;
        CompositionFrameGroupIndex = 0;
        CompositionFrame = 0;
        CompositionLayer = 0;
        CompositionPatternX = 0;
        CompositionPatternY = 0;
        CompositionPatternZ = 0;
        NotifyAllCompositionLabels();
        BuildCompositionGrid();
        BuildFilmstrip();
    }

    private void ReloadComposition()
    {
        if (_currentCompositionThing != null)
        {
            BuildCompositionGrid();
            if (ShowAllFrames) BuildFilmstrip();
        }
    }

    private void ReloadCompositionGridOnly()
    {
        if (_currentCompositionThing != null)
            BuildCompositionGrid();
    }

    private void BuildCompositionGrid()
    {
        CompositionSprites.Clear();

        var thing = _currentCompositionThing;
        if (thing == null || thing.FrameGroups.Length == 0)
        {
            CompositionGridColumns = 1;
            CompositionGridRows = 1;
            CompositionExactSize = 32;
            return;
        }

        int fgIdx = Math.Clamp(CompositionFrameGroupIndex, 0, Math.Max(0, thing.FrameGroups.Length - 1));
        var fg = thing.FrameGroups[fgIdx];

        if (fg.Width == 0 || fg.Height == 0)
        {
            CompositionGridColumns = 1;
            CompositionGridRows = 1;
            CompositionExactSize = 32;
            return;
        }

        int frame = Math.Clamp(CompositionFrame, 0, Math.Max(0, fg.Frames - 1));
        int layer = Math.Clamp(CompositionLayer, 0, Math.Max(0, fg.Layers - 1));
        int pz = Math.Clamp(CompositionPatternZ, 0, Math.Max(0, fg.PatternZ - 1));

        // OB behavior: items/effects show ALL patterns at once; outfits/missiles show one pattern
        bool showAllPatterns = thing.Category != ThingCategory.Outfit && thing.Category != ThingCategory.Missile;
        int pxCount = showAllPatterns ? fg.PatternX : 1;
        int pyCount = showAllPatterns ? fg.PatternY : 1;
        int pxStart = showAllPatterns ? 0 : Math.Clamp(CompositionPatternX, 0, Math.Max(0, fg.PatternX - 1));
        int pyStart = showAllPatterns ? 0 : Math.Clamp(CompositionPatternY, 0, Math.Max(0, fg.PatternY - 1));

        CompositionGridColumns = fg.Width * pxCount;
        CompositionGridRows = fg.Height * pyCount;
        CompositionExactSize = fg.ExactSize;

        // Build grid: for each pattern cell (py, px), render W×H sprites
        // Tibia coordinate system: w=0,h=0 is bottom-right; we render top-left to bottom-right
        for (int patY = 0; patY < pyCount; patY++)
        {
            for (int row = 0; row < fg.Height; row++)
            {
                for (int patX = 0; patX < pxCount; patX++)
                {
                    for (int col = 0; col < fg.Width; col++)
                    {
                        int px = pxStart + patX;
                        int py = pyStart + patY;
                        uint spriteId = fg.GetSpriteId(fg.Width - 1 - col, fg.Height - 1 - row, layer, px, py, pz, frame);
                        var svm = new SpriteViewModel { SpriteId = spriteId };
                        svm.Bitmap = LoadSpriteBitmap(spriteId);
                        CompositionSprites.Add(svm);
                    }
                }
            }
        }
    }

    [RelayCommand]
    private void NavigateRightSpriteToId(uint spriteId)
    {
        if (_sprFile == null || spriteId == 0) return;

        int page = (int)((spriteId - 1) / RightSpritesPerPage) + 1;
        if (page != RightSpriteCurrentPage)
        {
            RightSpriteCurrentPage = page;
            LoadRightSpritePage();
        }

        SelectedRightSprite = RightSprites.FirstOrDefault(s => s.SpriteId == spriteId);
    }

    // ══════════════════════════════════════════════════════════════════════
    // ── Reset thing to original state ──
    // ══════════════════════════════════════════════════════════════════════

    [RelayCommand]
    private void ResetThing()
    {
        var thing = _currentCompositionThing;
        if (thing == null || SelectedClientItem == null) return;

        var key = (thing.Category, thing.Id);
        if (!_originalSnapshots.TryGetValue(key, out var original)) return;

        // Restore from snapshot
        var restored = original.Clone();
        SelectedClientItem.ThingType.FrameGroups = restored.FrameGroups;
        CopyThingFlags(restored, SelectedClientItem.ThingType);

        // Re-snapshot (so another reset restores to original, not to the clone)
        _originalSnapshots[key] = original;

        // Reload
        StopCompositionAnimTimer();
        IsPlayingAnimation = false;
        CompositionFrameGroupIndex = 0;
        CompositionFrame = 0;
        CompositionLayer = 0;
        CompositionPatternX = 0;
        CompositionPatternY = 0;
        CompositionPatternZ = 0;
        NotifyAllCompositionLabels();
        OnPropertyChanged(nameof(IsItemSelected));
        OnPropertyChanged(nameof(IsOutfitSelected));
        OnPropertyChanged(nameof(IsEffectSelected));
        OnPropertyChanged(nameof(IsMissileSelected));
        OnPropertyChanged(nameof(IsNotMissileSelected));
        ReloadComposition();
        StatusText = $"Reset item {thing.Id} to original state";
    }

    private static void CopyThingFlags(DatThingType src, DatThingType dst)
    {
        dst.IsGround = src.IsGround; dst.GroundSpeed = src.GroundSpeed;
        dst.IsGroundBorder = src.IsGroundBorder; dst.IsOnBottom = src.IsOnBottom; dst.IsOnTop = src.IsOnTop;
        dst.IsContainer = src.IsContainer; dst.IsStackable = src.IsStackable;
        dst.IsForceUse = src.IsForceUse; dst.IsMultiUse = src.IsMultiUse;
        dst.IsWritable = src.IsWritable; dst.IsWritableOnce = src.IsWritableOnce;
        dst.MaxTextLength = src.MaxTextLength;
        dst.IsFluidContainer = src.IsFluidContainer; dst.IsFluid = src.IsFluid;
        dst.IsUnpassable = src.IsUnpassable; dst.IsUnmoveable = src.IsUnmoveable;
        dst.IsBlockMissile = src.IsBlockMissile; dst.IsBlockPathfind = src.IsBlockPathfind;
        dst.IsNoMoveAnimation = src.IsNoMoveAnimation; dst.IsPickupable = src.IsPickupable;
        dst.IsHangable = src.IsHangable; dst.IsVertical = src.IsVertical; dst.IsHorizontal = src.IsHorizontal;
        dst.IsRotatable = src.IsRotatable;
        dst.HasLight = src.HasLight; dst.LightLevel = src.LightLevel; dst.LightColor = src.LightColor;
        dst.IsDontHide = src.IsDontHide; dst.IsTranslucent = src.IsTranslucent;
        dst.HasOffset = src.HasOffset; dst.OffsetX = src.OffsetX; dst.OffsetY = src.OffsetY;
        dst.HasElevation = src.HasElevation; dst.Elevation = src.Elevation;
        dst.IsLyingObject = src.IsLyingObject; dst.IsAnimateAlways = src.IsAnimateAlways;
        dst.IsMiniMap = src.IsMiniMap; dst.MiniMapColor = src.MiniMapColor;
        dst.IsLensHelp = src.IsLensHelp; dst.LensHelp = src.LensHelp;
        dst.IsFullGround = src.IsFullGround; dst.IsIgnoreLook = src.IsIgnoreLook;
        dst.IsCloth = src.IsCloth; dst.ClothSlot = src.ClothSlot;
        dst.IsMarketItem = src.IsMarketItem;
        dst.MarketCategory = src.MarketCategory; dst.MarketTradeAs = src.MarketTradeAs;
        dst.MarketShowAs = src.MarketShowAs; dst.MarketName = src.MarketName;
        dst.MarketRestrictProfession = src.MarketRestrictProfession; dst.MarketRestrictLevel = src.MarketRestrictLevel;
        dst.HasDefaultAction = src.HasDefaultAction; dst.DefaultAction = src.DefaultAction;
        dst.IsWrappable = src.IsWrappable; dst.IsUnwrappable = src.IsUnwrappable;
        dst.IsTopEffect = src.IsTopEffect; dst.IsUsable = src.IsUsable;
    }

    // ══════════════════════════════════════════════════════════════════════
    // ── Thing CRUD Commands ──
    // ══════════════════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task ReplaceThingAsync()
    {
        if (SelectedClientItem == null || _datData == null) return;
        StatusText = "Replace: not yet implemented — DAT write support needed";
    }

    [RelayCommand]
    private async Task ImportThingAsync()
    {
        if (_datData == null) return;
        StatusText = "Import: not yet implemented — DAT write support needed";
    }

    [RelayCommand]
    private async Task ExportThingAsync()
    {
        if (SelectedClientItem == null || _sprFile == null) return;

        var path = await FileDialogHelper.SaveFileAsync("Export Thing", [("PNG Image", "*.png")]);
        if (path == null) return;

        try
        {
            var bitmap = ComposeThingBitmap(SelectedClientItem.ThingType);
            if (bitmap == null) { StatusText = "Nothing to export"; return; }
            using var fs = File.Create(path);
            SaveWriteableBitmapAsPng(bitmap, fs);
            StatusText = $"Exported: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Export error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void EditThing()
    {
        OpenClientItemEditor();
    }

    [RelayCommand]
    private void DuplicateThing()
    {
        if (SelectedClientItem == null || _datData == null) return;
        StatusText = "Duplicate: not yet implemented — DAT write support needed";
    }

    [RelayCommand]
    private void NewThing()
    {
        if (_datData == null) return;
        StatusText = "New: not yet implemented — DAT write support needed";
    }

    [RelayCommand]
    private void RemoveThing()
    {
        if (SelectedClientItem == null || _datData == null) return;
        StatusText = "Remove: not yet implemented — DAT write support needed";
    }

    // ══════════════════════════════════════════════════════════════════════
    // ── Sprite CRUD Commands ──
    // ══════════════════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task ReplaceSpriteAsync()
    {
        if (SelectedRightSprite == null || _sprFile == null) return;
        StatusText = "Replace sprite: not yet implemented — SPR write support needed";
    }

    [RelayCommand]
    private async Task ImportSpriteAsync()
    {
        if (_sprFile == null) return;
        StatusText = "Import sprite: not yet implemented — SPR write support needed";
    }

    [RelayCommand]
    private async Task ExportSpriteAsync()
    {
        if (SelectedRightSprite == null || _sprFile == null) return;

        var path = await FileDialogHelper.SaveFileAsync("Export Sprite", [("PNG Image", "*.png")]);
        if (path == null) return;

        try
        {
            var bitmap = SelectedRightSprite.Bitmap ?? LoadSpriteBitmap(SelectedRightSprite.SpriteId);
            if (bitmap == null) { StatusText = "Nothing to export"; return; }
            using var fs = File.Create(path);
            SaveWriteableBitmapAsPng(bitmap, fs);
            StatusText = $"Exported sprite {SelectedRightSprite.SpriteId}: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Export sprite error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void NewSprite()
    {
        if (_sprFile == null) return;
        StatusText = "New sprite: not yet implemented — SPR write support needed";
    }

    [RelayCommand]
    private void RemoveSprite()
    {
        if (SelectedRightSprite == null || _sprFile == null) return;
        StatusText = "Remove sprite: not yet implemented — SPR write support needed";
    }

    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Load a single sprite to a WriteableBitmap.</summary>
    internal WriteableBitmap? LoadSpriteBitmap(uint spriteId)
    {
        if (_sprFile == null || spriteId == 0) return null;

        try
        {
            var rgba = _sprFile.GetSpriteRgba(spriteId);
            if (rgba == null) return null;

            var bitmap = new WriteableBitmap(
                new PixelSize(32, 32),
                new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Rgba8888,
                Avalonia.Platform.AlphaFormat.Unpremul);

            using (var fb = bitmap.Lock())
            {
                Marshal.Copy(rgba, 0, fb.Address, rgba.Length);
            }
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Compose all W×H tiles of a ThingType into a single bitmap (like Object Builder).
    /// Uses frame 0, all layers, outfits show south-facing (patternX=2 if available).
    /// </summary>
    internal WriteableBitmap? ComposeThingBitmap(DatThingType thing, int frame = 0)
    {
        if (_sprFile == null || thing.FrameGroups.Length == 0) return null;

        var fg = thing.FrameGroups[0];
        int w = fg.Width;
        int h = fg.Height;
        if (w == 0 || h == 0) return null;

        int clampedFrame = Math.Clamp(frame, 0, Math.Max(0, fg.Frames - 1));

        // Single 1×1 item — use simple path
        if (w == 1 && h == 1 && fg.Layers == 1)
        {
            int px = 0;
            // Outfits: show south-facing (patternX index 2 if available)
            if (thing.Category == ThingCategory.Outfit && fg.PatternX > 2)
                px = 2;
            uint sprId = fg.GetSpriteId(0, 0, 0, px, 0, 0, clampedFrame);
            return LoadSpriteBitmap(sprId);
        }

        int bmpW = w * 32;
        int bmpH = h * 32;
        var pixels = new byte[bmpW * bmpH * 4];

        int layers = fg.Layers;
        int patX = 0;
        if (thing.Category == ThingCategory.Outfit)
        {
            layers = 1; // Only base layer for outfits in list view
            if (fg.PatternX > 2) patX = 2; // South-facing
        }

        for (int l = 0; l < layers; l++)
        {
            for (int tw = 0; tw < w; tw++)
            {
                for (int th = 0; th < h; th++)
                {
                    uint sprId = fg.GetSpriteId(tw, th, l, patX, 0, 0, clampedFrame);
                    var rgba = _sprFile.GetSpriteRgba(sprId);
                    if (rgba == null) continue;

                    // Place with inverted coords (Object Builder style)
                    int destX = (w - 1 - tw) * 32;
                    int destY = (h - 1 - th) * 32;

                    for (int y = 0; y < 32; y++)
                    {
                        for (int x = 0; x < 32; x++)
                        {
                            int srcIdx = (y * 32 + x) * 4;
                            byte a = rgba[srcIdx + 3];
                            if (a == 0) continue; // Skip transparent

                            int dstIdx = ((destY + y) * bmpW + destX + x) * 4;
                            pixels[dstIdx] = rgba[srcIdx];
                            pixels[dstIdx + 1] = rgba[srcIdx + 1];
                            pixels[dstIdx + 2] = rgba[srcIdx + 2];
                            pixels[dstIdx + 3] = a;
                        }
                    }
                }
            }
        }

        try
        {
            var bitmap = new WriteableBitmap(
                new PixelSize(bmpW, bmpH),
                new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Rgba8888,
                Avalonia.Platform.AlphaFormat.Unpremul);

            using (var fb = bitmap.Lock())
                Marshal.Copy(pixels, 0, fb.Address, pixels.Length);

            return bitmap;
        }
        catch { return null; }
    }

    /// <summary>Save a WriteableBitmap as PNG using Avalonia's rendering.</summary>
    private static void SaveWriteableBitmapAsPng(WriteableBitmap wb, Stream stream)
    {
        var renderTarget = new Avalonia.Media.Imaging.RenderTargetBitmap(wb.PixelSize);
        using (var ctx = renderTarget.CreateDrawingContext())
        {
            ctx.DrawImage(wb, new Rect(0, 0, wb.PixelSize.Width, wb.PixelSize.Height));
        }
        renderTarget.Save(stream);
    }
}
