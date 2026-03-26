using CommunityToolkit.Mvvm.ComponentModel;
using POriginsItemEditor.OTB;

namespace POriginsItemEditor.App.ViewModels;

public enum SplitMode { None, Right, Down }

/// <summary>
/// Represents a single editor session — one set of OTB/DAT/SPR/OTBM files
/// tied to a specific protocol version. Multiple sessions can coexist in tabs.
/// </summary>
public partial class SessionViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "New Session";
    [ObservableProperty] private bool _isActive;

    // ── Protocol info ──
    public int ProtocolVersion { get; set; }

    // ── Data files (owned by this session) ──
    public OtbData? OtbData { get; set; }
    public DatData? DatData { get; set; }
    public SprFile? SprFile { get; set; }
    public string? OtbPath { get; set; }
    public string? ClientFolderPath { get; set; }
    public MapData? MapData { get; set; }
    public string? MapFilePath { get; set; }
    public BrushDatabase? BrushDb { get; set; }
    public PaletteViewModel? Palette { get; set; }

    // ── Cached item lists ──
    public List<ItemViewModel> AllItems { get; set; } = [];
    public List<ClientItemViewModel> AllClientItems { get; set; } = [];

    // ── View-state preservation ──
    public Dictionary<(ThingCategory, ushort), DatThingType> OriginalSnapshots { get; } = [];
    public bool HasUnsavedChanges { get; set; }
    public bool IsClientLoaded { get; set; }
    public bool MapHasUnsavedChanges { get; set; }

    // ── Map viewport state ──
    public double MapViewX { get; set; }
    public double MapViewY { get; set; }
    public byte MapCurrentFloor { get; set; } = 7;
    public double MapZoom { get; set; } = 1.0;

    // ── UI state preservation (selection, page, filters) ──
    public ushort? SelectedClientItemId { get; set; }
    public ushort? SelectedOtbItemServerId { get; set; }
    public int ClientCurrentPage { get; set; } = 1;
    public int OtbPanelCurrentPage { get; set; } = 1;
    public int RightSpriteCurrentPage { get; set; } = 1;
    public string ClientCategoryFilter { get; set; } = "All";
    public string ClientSearchText { get; set; } = string.Empty;
    public string SearchText { get; set; } = string.Empty;

    /// <summary>Builds a display name from loaded files and protocol version.</summary>
    public void UpdateName()
    {
        var parts = new List<string>();
        if (ProtocolVersion > 0) parts.Add($"v{ProtocolVersion}");
        if (!string.IsNullOrEmpty(OtbPath))
            parts.Add(Path.GetFileName(OtbPath));
        else if (!string.IsNullOrEmpty(ClientFolderPath))
            parts.Add(Path.GetFileName(ClientFolderPath));
        Name = parts.Count > 0 ? string.Join(" — ", parts) : "New Session";
    }
}
