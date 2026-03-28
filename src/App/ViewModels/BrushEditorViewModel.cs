using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetsAndMapEditor.App.ViewModels;

/// <summary>Wraps a ChanceItem for data-binding in the brush editor.</summary>
public partial class BrushItemEntryViewModel : ObservableObject
{
    [ObservableProperty] private ushort _serverId;
    [ObservableProperty] private int _chance;
    [ObservableProperty] private WriteableBitmap? _sprite;
    public string Tooltip => $"[{ServerId}] chance={Chance}";
    public string IdLabel => ServerId > 0 ? ServerId.ToString() : "?";
}

/// <summary>Wraps a WallDoor for data-binding.</summary>
public partial class DoorEntryViewModel : ObservableObject
{
    [ObservableProperty] private ushort _serverId;
    [ObservableProperty] private string _doorType = "";
    [ObservableProperty] private bool _open;
    [ObservableProperty] private bool _locked;
    [ObservableProperty] private WriteableBitmap? _sprite;
    public string IdLabel => ServerId > 0 ? ServerId.ToString() : "?";
}

/// <summary>Wraps a wall segment (horizontal/vertical/corner/pole).</summary>
public partial class WallSegmentViewModel : ObservableObject
{
    [ObservableProperty] private string _segmentType = "";
    public ObservableCollection<BrushItemEntryViewModel> Items { get; } = [];
    public ObservableCollection<DoorEntryViewModel> Doors { get; } = [];

    public string SegmentIcon => SegmentType switch
    {
        "horizontal" => "━━",
        "vertical" => "┃",
        "corner" => "┗",
        "pole" => "╋",
        _ => "?",
    };
}

/// <summary>Wraps a single edge slot in a border definition (e.g. "n" → item 891).</summary>
public partial class BorderEdgeViewModel : ObservableObject
{
    [ObservableProperty] private string _edgeName = "";
    [ObservableProperty] private ushort _itemId;
    [ObservableProperty] private WriteableBitmap? _sprite;
    public string Label => EdgeName switch
    {
        "n" => "N", "s" => "S", "e" => "E", "w" => "W",
        "cnw" => "CNW", "cne" => "CNE", "csw" => "CSW", "cse" => "CSE",
        "dnw" => "DNW", "dne" => "DNE", "dsw" => "DSW", "dse" => "DSE",
        _ => EdgeName.ToUpperInvariant(),
    };
    public string IdLabel => ItemId > 0 ? ItemId.ToString() : "—";
    public bool HasItem => ItemId > 0;
}

/// <summary>Wraps a border reference on a ground brush, with optional resolved edge details.</summary>
public partial class BorderRefViewModel : ObservableObject
{
    [ObservableProperty] private string _align = "";
    [ObservableProperty] private int _borderId;
    [ObservableProperty] private string? _to;
    [ObservableProperty] private bool _isExpanded;

    /// <summary>Resolved edges from the catalog's BorderDef (for display/edit).</summary>
    public ObservableCollection<BorderEdgeViewModel> Edges { get; } = [];
    public bool HasEdges => Edges.Count > 0;
    public string Summary => $"{Align} #{BorderId}" + (To != null ? $" → {To}" : "");
}

/// <summary>Wraps a single brush of any type for listing in the brush editor.</summary>
public partial class BrushListItemViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _brushType = "";
    [ObservableProperty] private ushort _lookId;
    [ObservableProperty] private WriteableBitmap? _sprite;
    public string Tooltip => $"[{BrushType}] {Name} (look={LookId})";
    public bool HasSprite => Sprite != null;
    public string LookLabel => LookId > 0 ? LookId.ToString() : "";

    public GroundBrushDef? GroundDef { get; set; }
    public WallBrushDef? WallDef { get; set; }
    public DoodadBrushDef? DoodadDef { get; set; }
    public CreatureDef? CreatureDef { get; set; }

    public int ItemCount
    {
        get
        {
            if (GroundDef != null) return GroundDef.Items.Count;
            if (WallDef != null) return WallDef.Segments.Values.Sum(s => s.Items.Count);
            if (DoodadDef != null) return DoodadDef.Items.Count;
            return 0;
        }
    }
}

/// <summary>
/// ViewModel for the Brush Editor window — browse, create, and edit all brush types.
/// </summary>
public partial class BrushEditorViewModel : ObservableObject
{
    private readonly MainWindowViewModel _parent;
    private BrushCatalog? _catalog;
    private int _spriteHits;
    private int _spriteMisses;

    // All 12 edge names in display order
    private static readonly string[] EdgeNames =
        ["n", "e", "s", "w", "cnw", "cne", "csw", "cse", "dnw", "dne", "dsw", "dse"];

    public BrushEditorViewModel(MainWindowViewModel parent)
    {
        _parent = parent;
    }

    // ── Brush list ──
    public ObservableCollection<BrushListItemViewModel> AllBrushes { get; } = [];
    public ObservableCollection<BrushListItemViewModel> FilteredBrushes { get; } = [];
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _filterType = "All";
    [ObservableProperty] private BrushListItemViewModel? _selectedBrush;
    [ObservableProperty] private bool _hasBrushes;

    // ── Detail editing for selected brush ──
    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private ushort _editLookId;
    [ObservableProperty] private int _editZOrder;
    [ObservableProperty] private bool _editRandomize = true;
    [ObservableProperty] private bool _editDraggable;
    [ObservableProperty] private bool _editOnBlocking;
    [ObservableProperty] private string _editThickness = "1/1";

    // Ground items
    public ObservableCollection<BrushItemEntryViewModel> EditGroundItems { get; } = [];
    public ObservableCollection<BorderRefViewModel> EditBorders { get; } = [];
    public ObservableCollection<string> EditFriends { get; } = [];

    // Wall segments
    public ObservableCollection<WallSegmentViewModel> EditWallSegments { get; } = [];

    // Doodad items
    public ObservableCollection<BrushItemEntryViewModel> EditDoodadItems { get; } = [];

    // ── Stats ──
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _diagnosticText = "";

    public string[] BrushTypes => ["All", "ground", "wall", "doodad", "creature"];

    public void Initialize(BrushCatalog catalog)
    {
        _catalog = catalog;
        AllBrushes.Clear();
        _spriteHits = 0;
        _spriteMisses = 0;

        var otb = _parent.ExposedOtbData;
        var dat = _parent.ExposedDatData;
        Debug.WriteLine($"[BrushEditor] Initialize: OTB={(otb != null ? $"{otb.Items.Count} items" : "NULL")}, DAT={(dat != null ? $"{dat.Items.Count} items" : "NULL")}");
        Debug.WriteLine($"[BrushEditor] Catalog: {catalog.Grounds.Count} grounds, {catalog.Walls.Count} walls, {catalog.Doodads.Count} doodads, {catalog.Creatures.Count} creatures, {catalog.Borders.Count} borders");

        foreach (var g in catalog.Grounds)
            AllBrushes.Add(CreateListItem(g.Name, "ground", g.LookId, groundDef: g));
        foreach (var w in catalog.Walls)
            AllBrushes.Add(CreateListItem(w.Name, "wall", w.LookId, wallDef: w));
        foreach (var d in catalog.Doodads)
            AllBrushes.Add(CreateListItem(d.Name, "doodad", d.LookId, doodadDef: d));
        foreach (var c in catalog.Creatures)
            AllBrushes.Add(CreateListItem(c.Name, "creature", 0, creatureDef: c));

        HasBrushes = AllBrushes.Count > 0;
        StatusText = $"{AllBrushes.Count} brushes loaded";
        DiagnosticText = $"Sprites: {_spriteHits} ok, {_spriteMisses} miss | {catalog.Borders.Count} borders | OTB: {(otb != null ? "OK" : "–")} DAT: {(dat != null ? "OK" : "–")}";

        ApplyFilter();

        if (FilteredBrushes.Count > 0)
            SelectedBrush = FilteredBrushes[0];
    }

    private BrushListItemViewModel CreateListItem(string name, string type, ushort lookId,
        GroundBrushDef? groundDef = null, WallBrushDef? wallDef = null,
        DoodadBrushDef? doodadDef = null, CreatureDef? creatureDef = null)
    {
        return new BrushListItemViewModel
        {
            Name = name,
            BrushType = type,
            LookId = lookId,
            Sprite = lookId > 0 ? GetSprite(lookId) : null,
            GroundDef = groundDef,
            WallDef = wallDef,
            DoodadDef = doodadDef,
            CreatureDef = creatureDef,
        };
    }

    internal WriteableBitmap? GetSprite(ushort serverId)
    {
        var otb = _parent.ExposedOtbData;
        var dat = _parent.ExposedDatData;
        if (otb == null || dat == null) { _spriteMisses++; return null; }

        var item = otb.Items.FirstOrDefault(i => i.ServerId == serverId);
        if (item == null || item.ClientId == 0) { _spriteMisses++; return null; }

        if (dat.Items.TryGetValue(item.ClientId, out var thing))
        {
            var bmp = _parent.ComposeThingBitmap(thing);
            if (bmp != null) { _spriteHits++; return bmp; }
        }

        _spriteMisses++;
        return null;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnFilterTypeChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        FilteredBrushes.Clear();
        var search = SearchText.Trim();
        foreach (var b in AllBrushes)
        {
            if (FilterType != "All" && b.BrushType != FilterType) continue;
            if (search.Length > 0 && !b.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
            FilteredBrushes.Add(b);
        }
        HasBrushes = FilteredBrushes.Count > 0;
        StatusText = $"{FilteredBrushes.Count}/{AllBrushes.Count} brushes";
    }

    partial void OnSelectedBrushChanged(BrushListItemViewModel? value)
    {
        LoadBrushDetails(value);
    }

    private void LoadBrushDetails(BrushListItemViewModel? item)
    {
        EditGroundItems.Clear();
        EditBorders.Clear();
        EditFriends.Clear();
        EditWallSegments.Clear();
        EditDoodadItems.Clear();

        if (item == null) return;

        EditName = item.Name;
        EditLookId = item.LookId;

        if (item.GroundDef is { } g)
        {
            EditZOrder = g.ZOrder;
            EditRandomize = g.Randomize;
            foreach (var ci in g.Items)
                EditGroundItems.Add(new BrushItemEntryViewModel
                {
                    ServerId = ci.Id,
                    Chance = ci.Chance,
                    Sprite = GetSprite(ci.Id),
                });
            foreach (var br in g.Borders)
            {
                var vm = new BorderRefViewModel
                {
                    Align = br.Align,
                    BorderId = br.BorderId,
                    To = br.To,
                };
                // Resolve edges from catalog
                if (_catalog != null && _catalog.Borders.TryGetValue(br.BorderId, out var borderDef))
                {
                    foreach (var edge in EdgeNames)
                    {
                        var edgeVm = new BorderEdgeViewModel { EdgeName = edge };
                        if (borderDef.Edges.TryGetValue(edge, out var itemId))
                        {
                            edgeVm.ItemId = itemId;
                            edgeVm.Sprite = GetSprite(itemId);
                        }
                        vm.Edges.Add(edgeVm);
                    }
                }
                // Handle inline borders
                else if (br.Inline && br.InlineEdges.Count > 0)
                {
                    foreach (var edge in EdgeNames)
                    {
                        var edgeVm = new BorderEdgeViewModel { EdgeName = edge };
                        if (br.InlineEdges.TryGetValue(edge, out var itemId))
                        {
                            edgeVm.ItemId = itemId;
                            edgeVm.Sprite = GetSprite(itemId);
                        }
                        vm.Edges.Add(edgeVm);
                    }
                }
                EditBorders.Add(vm);
            }
            foreach (var f in g.Friends)
                EditFriends.Add(f);
        }

        if (item.WallDef is { } w)
        {
            foreach (var (segType, seg) in w.Segments)
            {
                var segVm = new WallSegmentViewModel { SegmentType = segType };
                foreach (var ci in seg.Items)
                    segVm.Items.Add(new BrushItemEntryViewModel
                    {
                        ServerId = ci.Id,
                        Chance = ci.Chance,
                        Sprite = GetSprite(ci.Id),
                    });
                foreach (var d in seg.Doors)
                    segVm.Doors.Add(new DoorEntryViewModel
                    {
                        ServerId = d.Id,
                        DoorType = d.Type,
                        Open = d.Open,
                        Locked = d.Locked,
                        Sprite = GetSprite(d.Id),
                    });
                EditWallSegments.Add(segVm);
            }
        }

        if (item.DoodadDef is { } d2)
        {
            EditDraggable = d2.Draggable;
            EditOnBlocking = d2.OnBlocking;
            EditThickness = $"{d2.ThicknessNum}/{d2.ThicknessDen}";
            foreach (var ci in d2.Items)
                EditDoodadItems.Add(new BrushItemEntryViewModel
                {
                    ServerId = ci.Id,
                    Chance = ci.Chance,
                    Sprite = GetSprite(ci.Id),
                });
        }
    }

    // ── CRUD ──

    [RelayCommand]
    private void ToggleBorderExpand(BorderRefViewModel? border)
    {
        if (border != null) border.IsExpanded = !border.IsExpanded;
    }

    [RelayCommand]
    private void SaveBorderEdges(BorderRefViewModel? border)
    {
        if (border == null || _catalog == null) return;

        // Write edge changes back to the catalog's BorderDef
        if (_catalog.Borders.TryGetValue(border.BorderId, out var borderDef))
        {
            borderDef.Edges.Clear();
            foreach (var edgeVm in border.Edges)
            {
                if (edgeVm.ItemId > 0)
                    borderDef.Edges[edgeVm.EdgeName] = edgeVm.ItemId;
            }
            StatusText = $"Border #{border.BorderId} edges saved";
        }
    }

    [RelayCommand]
    private void SaveBrush()
    {
        if (SelectedBrush == null || _catalog == null) return;

        SelectedBrush.Name = EditName;
        SelectedBrush.LookId = EditLookId;
        SelectedBrush.Sprite = EditLookId > 0 ? GetSprite(EditLookId) : null;

        if (SelectedBrush.GroundDef is { } g)
        {
            g.Name = EditName;
            g.LookId = EditLookId;
            g.ZOrder = EditZOrder;
            g.Randomize = EditRandomize;
            g.Items.Clear();
            foreach (var entry in EditGroundItems)
                g.Items.Add(new ChanceItem { Id = entry.ServerId, Chance = entry.Chance });
            g.Borders.Clear();
            foreach (var br in EditBorders)
                g.Borders.Add(new GroundBorderRef { Align = br.Align, BorderId = br.BorderId, To = br.To });
            g.Friends.Clear();
            g.Friends.AddRange(EditFriends);
        }

        if (SelectedBrush.WallDef is { } w)
        {
            w.Name = EditName;
            w.LookId = EditLookId;
            w.Segments.Clear();
            foreach (var segVm in EditWallSegments)
            {
                var seg = new WallSegment();
                foreach (var ci in segVm.Items)
                    seg.Items.Add(new ChanceItem { Id = ci.ServerId, Chance = ci.Chance });
                foreach (var d in segVm.Doors)
                    seg.Doors.Add(new WallDoor { Id = d.ServerId, Type = d.DoorType, Open = d.Open, Locked = d.Locked });
                w.Segments[segVm.SegmentType] = seg;
            }
        }

        if (SelectedBrush.DoodadDef is { } d2)
        {
            d2.Name = EditName;
            d2.LookId = EditLookId;
            d2.Draggable = EditDraggable;
            d2.OnBlocking = EditOnBlocking;
            d2.Items.Clear();
            foreach (var entry in EditDoodadItems)
                d2.Items.Add(new ChanceItem { Id = entry.ServerId, Chance = entry.Chance });
            var parts = EditThickness.Split('/');
            if (parts.Length == 2 && int.TryParse(parts[0], out var num) && int.TryParse(parts[1], out var den))
            {
                d2.ThicknessNum = num;
                d2.ThicknessDen = den;
            }
        }

        _catalog.BuildIndexes();
        StatusText = $"Saved: {EditName}";
    }

    [RelayCommand]
    private void AddGroundItem()
    {
        EditGroundItems.Add(new BrushItemEntryViewModel { ServerId = 0, Chance = 10 });
    }

    [RelayCommand]
    private void RemoveGroundItem(BrushItemEntryViewModel? item)
    {
        if (item != null) EditGroundItems.Remove(item);
    }

    [RelayCommand]
    private void AddDoodadItem()
    {
        EditDoodadItems.Add(new BrushItemEntryViewModel { ServerId = 0, Chance = 10 });
    }

    [RelayCommand]
    private void RemoveDoodadItem(BrushItemEntryViewModel? item)
    {
        if (item != null) EditDoodadItems.Remove(item);
    }

    [RelayCommand]
    private void AddBorderRef()
    {
        var vm = new BorderRefViewModel { Align = "outer", BorderId = 1, IsExpanded = true };
        // Resolve edges if border exists
        if (_catalog != null && _catalog.Borders.TryGetValue(1, out var borderDef))
        {
            foreach (var edge in EdgeNames)
            {
                var edgeVm = new BorderEdgeViewModel { EdgeName = edge };
                if (borderDef.Edges.TryGetValue(edge, out var itemId))
                {
                    edgeVm.ItemId = itemId;
                    edgeVm.Sprite = GetSprite(itemId);
                }
                vm.Edges.Add(edgeVm);
            }
        }
        EditBorders.Add(vm);
    }

    [RelayCommand]
    private void RemoveBorderRef(BorderRefViewModel? item)
    {
        if (item != null) EditBorders.Remove(item);
    }

    [RelayCommand]
    private void AddWallSegment()
    {
        EditWallSegments.Add(new WallSegmentViewModel { SegmentType = "horizontal" });
    }

    [RelayCommand]
    private void NewGroundBrush()
    {
        if (_catalog == null) return;
        var def = new GroundBrushDef { Name = "New Ground Brush", LookId = 0, ZOrder = 100 };
        _catalog.Grounds.Add(def);
        var vm = CreateListItem(def.Name, "ground", def.LookId, groundDef: def);
        AllBrushes.Add(vm);
        ApplyFilter();
        SelectedBrush = vm;
    }

    [RelayCommand]
    private void NewWallBrush()
    {
        if (_catalog == null) return;
        var def = new WallBrushDef { Name = "New Wall Brush", LookId = 0 };
        _catalog.Walls.Add(def);
        var vm = CreateListItem(def.Name, "wall", def.LookId, wallDef: def);
        AllBrushes.Add(vm);
        ApplyFilter();
        SelectedBrush = vm;
    }

    [RelayCommand]
    private void NewDoodadBrush()
    {
        if (_catalog == null) return;
        var def = new DoodadBrushDef { Name = "New Doodad Brush", LookId = 0 };
        _catalog.Doodads.Add(def);
        var vm = CreateListItem(def.Name, "doodad", def.LookId, doodadDef: def);
        AllBrushes.Add(vm);
        ApplyFilter();
        SelectedBrush = vm;
    }

    [RelayCommand]
    private void DeleteBrush()
    {
        if (SelectedBrush == null || _catalog == null) return;
        var item = SelectedBrush;

        if (item.GroundDef != null) _catalog.Grounds.Remove(item.GroundDef);
        if (item.WallDef != null) _catalog.Walls.Remove(item.WallDef);
        if (item.DoodadDef != null) _catalog.Doodads.Remove(item.DoodadDef);

        AllBrushes.Remove(item);
        ApplyFilter();
        SelectedBrush = FilteredBrushes.FirstOrDefault();
        _catalog.BuildIndexes();
    }

    [RelayCommand]
    private void ExportAll()
    {
        if (_catalog == null) return;
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var brushDir = Path.Combine(baseDir, "data", "brushes");
        Directory.CreateDirectory(brushDir);
        BrushXmlWriter.SaveAll(_catalog, brushDir);
        StatusText = $"Exported {AllBrushes.Count} brushes to {brushDir}";
    }
}
