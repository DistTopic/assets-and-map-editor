using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AssetsAndMapEditor.OTB;

namespace AssetsAndMapEditor.App.ViewModels;

/// <summary>
/// A single item displayed in the palette grid — carries sprite + metadata for display.
/// </summary>
public partial class PaletteItemViewModel : ObservableObject
{
    public ushort ServerId { get; init; }
    public ushort ClientId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Article { get; init; }

    [ObservableProperty] private WriteableBitmap? _sprite;
    [ObservableProperty] private bool _isHighlighted;
    public int AnimFrame { get; set; }

    public string DisplayName => string.IsNullOrEmpty(Article) ? Name : $"{Article} {Name}";
    public string Tooltip => $"[{ServerId}] {DisplayName}";
}

/// <summary>
/// A sub-collection node inside a collection — holds sub-sub-collections and items.
/// </summary>
public partial class PaletteSubCollectionViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isRenaming;
    [ObservableProperty] private string _renameText = string.Empty;

    /// <summary>Direct items (legacy, still used for flat sub-collections).</summary>
    public ObservableCollection<PaletteItemViewModel> Items { get; } = [];

    /// <summary>Sub-sub-collections (3rd tier).</summary>
    public ObservableCollection<PaletteSubSubCollectionViewModel> SubSubCollections { get; } = [];

    public PaletteCollectionViewModel? Parent { get; init; }

    /// <summary>True for built-in entries like "Others" — cannot be renamed/deleted.</summary>
    public bool IsBuiltIn { get; init; }

    [RelayCommand]
    private void BeginRename()
    {
        if (IsBuiltIn) return;
        RenameText = Name;
        IsRenaming = true;
    }

    [RelayCommand]
    private void CommitRename()
    {
        if (!string.IsNullOrWhiteSpace(RenameText))
            Name = RenameText.Trim();
        IsRenaming = false;
    }

    [RelayCommand]
    private void CancelRename() => IsRenaming = false;
}

/// <summary>
/// A sub-sub-collection (3rd tier) inside a sub-collection — holds items.
/// </summary>
public partial class PaletteSubSubCollectionViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private bool _isRenaming;
    [ObservableProperty] private string _renameText = string.Empty;

    public ObservableCollection<PaletteItemViewModel> Items { get; } = [];

    public PaletteSubCollectionViewModel? Parent { get; init; }

    [RelayCommand]
    private void BeginRename()
    {
        RenameText = Name;
        IsRenaming = true;
    }

    [RelayCommand]
    private void CommitRename()
    {
        if (!string.IsNullOrWhiteSpace(RenameText))
            Name = RenameText.Trim();
        IsRenaming = false;
    }

    [RelayCommand]
    private void CancelRename() => IsRenaming = false;
}

/// <summary>
/// A custom brush: when active, paints randomly from its item set for tile variety.
/// </summary>
public partial class CustomBrushViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private bool _isRenaming;
    [ObservableProperty] private string _renameText = string.Empty;

    public ObservableCollection<PaletteItemViewModel> Items { get; } = [];

    [RelayCommand]
    private void BeginRename() { RenameText = Name; IsRenaming = true; }

    [RelayCommand]
    private void CommitRename()
    {
        if (!string.IsNullOrWhiteSpace(RenameText))
            Name = RenameText.Trim();
        IsRenaming = false;
    }

    [RelayCommand]
    private void CancelRename() => IsRenaming = false;
}

/// <summary>
/// A top-level collection in the palette tree — contains sub-collections.
/// </summary>
public partial class PaletteCollectionViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _icon = "fa-solid fa-layer-group";
    [ObservableProperty] private bool _isExpanded = true;
    [ObservableProperty] private bool _isRenaming;
    [ObservableProperty] private string _renameText = string.Empty;

    public ObservableCollection<PaletteSubCollectionViewModel> SubCollections { get; } = [];

    /// <summary>True for built-in entries like "Raw" — cannot be renamed/deleted.</summary>
    public bool IsBuiltIn { get; init; }

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void BeginRename()
    {
        if (IsBuiltIn) return;
        RenameText = Name;
        IsRenaming = true;
    }

    [RelayCommand]
    private void CommitRename()
    {
        if (!string.IsNullOrWhiteSpace(RenameText))
            Name = RenameText.Trim();
        IsRenaming = false;
    }

    [RelayCommand]
    private void CancelRename() => IsRenaming = false;
}

/// <summary>
/// Main palette ViewModel — manages collections, catalog browsing, search,
/// and wires up all the CRUD + item selection logic.
/// </summary>
public partial class PaletteViewModel : ObservableObject
{
    private readonly MainWindowViewModel _parent;
    private Dictionary<ushort, ushort> _serverToClientMap = [];
    private readonly Dictionary<ushort, WriteableBitmap?> _spriteCache = [];

    // ── Data ────────────────────────────────────────────────────
    public ObservableCollection<PaletteCollectionViewModel> Collections { get; } = [];

    // ── Custom brushes ──────────────────────────────────────────
    public ObservableCollection<CustomBrushViewModel> CustomBrushes { get; } = [];
    [ObservableProperty] private CustomBrushViewModel? _selectedBrush;
    [ObservableProperty] private bool _isEditingBrush;
    private static readonly Random _brushRandom = new();

    /// <summary>Pick a random server ID from the active brush, or 0 if none.</summary>
    public ushort GetBrushRandomItem()
    {
        if (SelectedBrush == null || SelectedBrush.Items.Count == 0) return 0;
        return SelectedBrush.Items[_brushRandom.Next(SelectedBrush.Items.Count)].ServerId;
    }

    // ── Selected state ──────────────────────────────────────────
    [ObservableProperty] private PaletteCollectionViewModel? _selectedCollection;
    [ObservableProperty] private PaletteSubCollectionViewModel? _selectedSubCollection;
    [ObservableProperty] private PaletteSubSubCollectionViewModel? _selectedSubSubCollection;
    [ObservableProperty] private PaletteItemViewModel? _selectedPaletteItem;

    /// <summary>The built-in "Raw" collection (always first, non-editable).</summary>
    public PaletteCollectionViewModel? RawCollection { get; private set; }
    /// <summary>The built-in "Others" sub-collection inside Raw (shows all items).</summary>
    public PaletteSubCollectionViewModel? OthersSubCollection { get; private set; }

    // ── Display items for the active sub-collection ─────────────
    public ObservableCollection<PaletteItemViewModel> DisplayedItems { get; } = [];

    // ── Collection tab: live reference to the selected collection's items ──
    [ObservableProperty] private ObservableCollection<PaletteItemViewModel>? _collectionViewSource;
    [ObservableProperty] private int _catalogTabIndex;

    // ── South panel state: catalog or sub-collection view ────────
    [ObservableProperty] private bool _isViewingSubCollection;
    [ObservableProperty] private string _viewingSubCollectionName = string.Empty;
    public bool IsViewingCatalog => !IsViewingSubCollection;

    // ── Catalog browsing ────────────────────────────────────────
    [ObservableProperty] private string _catalogSearchText = string.Empty;
    public ObservableCollection<PaletteItemViewModel> CatalogResults { get; } = [];
    private const int CatalogPageSize = 200;
    [ObservableProperty] private int _catalogPage;
    [ObservableProperty] private int _catalogTotalPages;
    [ObservableProperty] private string _catalogStatusText = string.Empty;
    private List<ushort> _filteredCatalogIds = [];
    private List<PaletteItemViewModel>? _filteredCatalogItems;
    public bool HasItems => _serverToClientMap.Count > 0;

    // ── Sub-collection search/filter ────────────────────────────
    [ObservableProperty] private string _itemSearchText = string.Empty;

    /// <summary>Callback to show a confirmation dialog. Returns true if the user confirmed.</summary>
    public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }

    public PaletteViewModel(MainWindowViewModel parent)
    {
        _parent = parent;
    }

    // ════════════════════════════════════════════════════════════
    //  Initialization
    // ════════════════════════════════════════════════════════════

    /// <summary>
    /// Called after client + OTB are loaded. Builds the server→client map,
    /// loads the catalog, and restores persisted collections.
    /// </summary>
    public void Initialize(OtbData? otbData, DatData? datData, SprFile? sprFile)
    {
        // Build server → client map from OTB
        _serverToClientMap.Clear();
        _spriteCache.Clear();
        if (otbData != null)
        {
            foreach (var item in otbData.Items)
            {
                if (item.ServerId > 0 && item.ClientId > 0)
                    _serverToClientMap[item.ServerId] = item.ClientId;
            }
        }

        OnPropertyChanged(nameof(HasItems));

        // Load persisted collections
        LoadFromConfig();

        // Show catalog in south panel
        IsViewingSubCollection = false;
        SearchCatalog();
    }

    /// <summary>Get a composed sprite thumbnail for a server ID (full width×height).</summary>
    private WriteableBitmap? GetSprite(ushort serverId)
    {
        if (_spriteCache.TryGetValue(serverId, out var cached))
            return cached;

        WriteableBitmap? bmp = null;
        if (_serverToClientMap.TryGetValue(serverId, out var clientId) && clientId > 0)
        {
            var datData = _parent.ExposedDatData;
            if (datData != null && datData.Items.TryGetValue(clientId, out var thing))
                bmp = _parent.ComposeThingBitmap(thing);
        }

        _spriteCache[serverId] = bmp;
        return bmp;
    }

    /// <summary>Look up the DatThingType for a palette item (used by animation timer).</summary>
    public DatThingType? GetThingForPaletteItem(PaletteItemViewModel pvm)
    {
        if (pvm.ClientId == 0) return null;
        var datData = _parent.ExposedDatData;
        if (datData != null && datData.Items.TryGetValue(pvm.ClientId, out var thing))
            return thing;
        return null;
    }

    /// <summary>Build a PaletteItemViewModel for a server ID.</summary>
    private PaletteItemViewModel? CreatePaletteItem(ushort serverId)
    {
        if (!_serverToClientMap.TryGetValue(serverId, out var clientId))
            return null;

        return new PaletteItemViewModel
        {
            ServerId = serverId,
            ClientId = clientId,
            Name = $"item #{serverId}",
            Sprite = GetSprite(serverId)
        };
    }

    // ════════════════════════════════════════════════════════════
    //  Tree selection
    // ════════════════════════════════════════════════════════════

    partial void OnSelectedSubCollectionChanged(PaletteSubCollectionViewModel? oldValue, PaletteSubCollectionViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        if (newValue != null) newValue.IsSelected = true;
        // Auto-select first sub-sub-collection (or null)
        SelectedSubSubCollection = newValue?.SubSubCollections.FirstOrDefault();
        // If no sub-sub-collections, refresh grid from sub-collection
        if (newValue?.SubSubCollections.Count == 0)
        {
            CatalogPage = 0;
            SearchCatalog();
        }
        UpdateCollectionViewSource();
    }

    partial void OnSelectedCollectionChanged(PaletteCollectionViewModel? value)
    {
        // Defer selection so the ComboBox finishes rebinding ItemsSource first;
        // otherwise the ComboBox clears SelectedItem after we set it.
        var first = value?.SubCollections.FirstOrDefault();
        Dispatcher.UIThread.Post(() => SelectedSubCollection = first);
    }

    partial void OnSelectedSubSubCollectionChanged(PaletteSubSubCollectionViewModel? value)
    {
        CatalogPage = 0;
        SearchCatalog();
        UpdateCollectionViewSource();
    }

    private void UpdateCollectionViewSource()
    {
        if (SelectedSubSubCollection != null)
            CollectionViewSource = SelectedSubSubCollection.Items;
        else if (SelectedSubCollection != null && SelectedSubCollection != OthersSubCollection)
            CollectionViewSource = SelectedSubCollection.Items;
        else
            CollectionViewSource = null;
    }

    partial void OnIsViewingSubCollectionChanged(bool value)
    {
        OnPropertyChanged(nameof(IsViewingCatalog));
    }

    partial void OnItemSearchTextChanged(string value) => RefreshDisplayedItems();

    private void RefreshDisplayedItems()
    {
        DisplayedItems.Clear();
        if (SelectedSubCollection == null) return;

        var search = ItemSearchText.Trim();
        foreach (var itemVm in SelectedSubCollection.Items)
        {
            if (search.Length > 0 &&
                !itemVm.Name.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !itemVm.ServerId.ToString().Contains(search))
                continue;
            DisplayedItems.Add(itemVm);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Catalog browsing
    // ════════════════════════════════════════════════════════════

    partial void OnCatalogSearchTextChanged(string value)
    {
        CatalogPage = 0;
        SearchCatalog();
    }

    /// <summary>Select a sub-collection and show its items in the grid.</summary>
    public void ShowSubCollectionItems(PaletteSubCollectionViewModel sub)
    {
        // Find parent collection and select it
        if (sub.Parent != null)
            SelectedCollection = sub.Parent;
        SelectedSubCollection = sub;
    }

    [RelayCommand]
    private void BackToCatalog()
    {
        IsViewingSubCollection = false;
        SearchCatalog();
    }

    [RelayCommand]
    private void CatalogNextPage()
    {
        if (CatalogPage < CatalogTotalPages - 1)
        {
            CatalogPage++;
            FillCatalogPage();
        }
    }

    [RelayCommand]
    private void CatalogPreviousPage()
    {
        if (CatalogPage > 0)
        {
            CatalogPage--;
            FillCatalogPage();
        }
    }

    [RelayCommand]
    private void CatalogFirstPage()
    {
        if (CatalogPage != 0)
        {
            CatalogPage = 0;
            FillCatalogPage();
        }
    }

    [RelayCommand]
    private void CatalogLastPage()
    {
        int last = CatalogTotalPages - 1;
        if (CatalogPage != last && last >= 0)
        {
            CatalogPage = last;
            FillCatalogPage();
        }
    }

    private void SearchCatalog()
    {
        if (_serverToClientMap.Count == 0) { CatalogResults.Clear(); CatalogStatusText = "No items — load OTB + Client"; return; }

        // Determine source based on selection hierarchy.
        // Built-in sub-collections show their pre-built items in the Catalog tab.
        // User-created collections show items only in the Collection tab —
        // the Catalog tab always falls back to the full OTB item list.
        IReadOnlyList<PaletteItemViewModel>? sourceItems = null;
        IEnumerable<ushort>? sourceIds = null;

        if (SelectedSubSubCollection != null && (SelectedSubCollection?.IsBuiltIn ?? false))
            sourceItems = SelectedSubSubCollection.Items;
        else if (SelectedSubCollection != null && SelectedSubCollection.IsBuiltIn && SelectedSubCollection != OthersSubCollection)
            sourceItems = SelectedSubCollection.Items;
        else
            sourceIds = _serverToClientMap.Keys; // Full catalog for "Others" and user collections

        var search = CatalogSearchText.Trim();

        // When we have pre-built items, filter them directly
        if (sourceItems != null)
        {
            _filteredCatalogIds = [];
            IEnumerable<PaletteItemViewModel> items = sourceItems;
            if (search.Length > 0)
            {
                items = items.Where(i =>
                    i.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    i.ServerId.ToString().Contains(search));
            }
            _filteredCatalogItems = items.ToList();
            CatalogTotalPages = Math.Max(1, (int)Math.Ceiling((double)_filteredCatalogItems.Count / CatalogPageSize));
            CatalogPage = 0;
            FillCatalogPage();
            return;
        }

        // ID-based path ("Others" / all OTB items)
        _filteredCatalogItems = null;

        if (search.Length == 0)
        {
            _filteredCatalogIds = sourceIds!.OrderBy(id => id).ToList();
        }
        else
        {
            bool startsWithWild = search.StartsWith('*');
            bool endsWithWild = search.EndsWith('*');
            var pattern = search.Trim('*');

            if (pattern.Length == 0)
            {
                _filteredCatalogIds = sourceIds!.OrderBy(id => id).ToList();
            }
            else if (startsWithWild && endsWithWild)
            {
                _filteredCatalogIds = sourceIds!
                    .Where(id => id.ToString().Contains(pattern))
                    .OrderBy(id => id).ToList();
            }
            else if (startsWithWild)
            {
                _filteredCatalogIds = sourceIds!
                    .Where(id => id.ToString().EndsWith(pattern))
                    .OrderBy(id => id).ToList();
            }
            else if (endsWithWild)
            {
                _filteredCatalogIds = sourceIds!
                    .Where(id => id.ToString().StartsWith(pattern))
                    .OrderBy(id => id).ToList();
            }
            else
            {
                if (ushort.TryParse(pattern, out var exactId) && sourceIds!.Contains(exactId))
                    _filteredCatalogIds = [exactId];
                else
                    _filteredCatalogIds = [];
            }
        }

        CatalogTotalPages = Math.Max(1, (int)Math.Ceiling((double)_filteredCatalogIds.Count / CatalogPageSize));
        CatalogPage = 0;
        FillCatalogPage();
    }

    private void FillCatalogPage()
    {
        CatalogResults.Clear();

        if (_filteredCatalogItems != null)
        {
            // Pre-built items from brush catalog sub-collections
            var page = _filteredCatalogItems
                .Skip(CatalogPage * CatalogPageSize)
                .Take(CatalogPageSize);
            foreach (var vm in page)
                CatalogResults.Add(vm);
            CatalogStatusText = $"{_filteredCatalogItems.Count} items — page {CatalogPage + 1}/{CatalogTotalPages}";
            return;
        }

        // ID-based path — recreate items from OTB mapping
        var idPage = _filteredCatalogIds
            .Skip(CatalogPage * CatalogPageSize)
            .Take(CatalogPageSize);

        foreach (var serverId in idPage)
        {
            var vm = CreatePaletteItem(serverId);
            if (vm != null) CatalogResults.Add(vm);
        }

        CatalogStatusText = $"{_filteredCatalogIds.Count} items — page {CatalogPage + 1}/{CatalogTotalPages}";
    }

    // ════════════════════════════════════════════════════════════
    //  Collection CRUD
    // ════════════════════════════════════════════════════════════

    [RelayCommand]
    private void AddCollection()
    {
        var col = new PaletteCollectionViewModel { Name = "New Collection" };
        Collections.Add(col);
        SelectedCollection = col;
        col.BeginRenameCommand.Execute(null);
        SaveToConfig();
    }

    [RelayCommand]
    private async Task RemoveCollectionAsync(PaletteCollectionViewModel? col)
    {
        if (col == null || col.IsBuiltIn) return;
        if (ConfirmAsync != null && !await ConfirmAsync("Delete Collection", $"Delete collection \"{col.Name}\"?"))
            return;
        Collections.Remove(col);
        if (SelectedCollection == col) SelectedCollection = Collections.FirstOrDefault();
        SaveToConfig();
    }

    [RelayCommand]
    private void AddSubCollection()
    {
        var col = SelectedCollection;
        if (col == null || col.IsBuiltIn) return;

        var sub = new PaletteSubCollectionViewModel { Name = "New Sub-Collection", Parent = col };
        col.SubCollections.Add(sub);
        col.IsExpanded = true;
        SelectedSubCollection = sub;
        sub.BeginRenameCommand.Execute(null);
        SaveToConfig();
    }

    [RelayCommand]
    private async Task RemoveSubCollectionAsync(PaletteSubCollectionViewModel? sub)
    {
        if (sub?.Parent == null || sub.IsBuiltIn) return;
        if (ConfirmAsync != null && !await ConfirmAsync("Delete Sub-Collection", $"Delete sub-collection \"{sub.Name}\"?"))
            return;
        sub.Parent.SubCollections.Remove(sub);
        if (SelectedSubCollection == sub)
            SelectedSubCollection = sub.Parent.SubCollections.FirstOrDefault();
        SaveToConfig();
    }

    [RelayCommand]
    private void AddSubSubCollection()
    {
        var sub = SelectedSubCollection;
        if (sub == null || sub.IsBuiltIn) return;

        var subSub = new PaletteSubSubCollectionViewModel { Name = "New Group", Parent = sub };
        sub.SubSubCollections.Add(subSub);
        SelectedSubSubCollection = subSub;
        subSub.BeginRenameCommand.Execute(null);
        SaveToConfig();
    }

    [RelayCommand]
    private async Task RemoveSubSubCollectionAsync(PaletteSubSubCollectionViewModel? subSub)
    {
        if (subSub?.Parent == null) return;
        if (ConfirmAsync != null && !await ConfirmAsync("Delete Group", $"Delete group \"{subSub.Name}\"?"))
            return;
        subSub.Parent.SubSubCollections.Remove(subSub);
        if (SelectedSubSubCollection == subSub)
            SelectedSubSubCollection = subSub.Parent.SubSubCollections.FirstOrDefault();
        SaveToConfig();
    }

    // ════════════════════════════════════════════════════════════
    //  Custom Brush CRUD
    // ════════════════════════════════════════════════════════════

    [RelayCommand]
    private void AddBrush()
    {
        var brush = new CustomBrushViewModel { Name = "New Brush" };
        CustomBrushes.Add(brush);
        SelectedBrush = brush;
        brush.BeginRenameCommand.Execute(null);
        SaveToConfig();
    }

    [RelayCommand]
    private async Task RemoveBrushAsync(CustomBrushViewModel? brush)
    {
        if (brush == null) return;
        if (ConfirmAsync != null && !await ConfirmAsync("Delete Brush", $"Delete brush \"{brush.Name}\"?"))
            return;
        CustomBrushes.Remove(brush);
        if (SelectedBrush == brush) SelectedBrush = CustomBrushes.FirstOrDefault();
        SaveToConfig();
    }

    [RelayCommand]
    private void EditBrush()
    {
        IsEditingBrush = SelectedBrush != null;
    }

    [RelayCommand]
    private void StopEditingBrush()
    {
        IsEditingBrush = false;
    }

    /// <summary>Add a catalog item to the currently selected brush.</summary>
    [RelayCommand]
    private void AddItemToBrush(PaletteItemViewModel? item)
    {
        if (item == null || SelectedBrush == null) return;
        if (SelectedBrush.Items.Any(i => i.ServerId == item.ServerId)) return;
        var vm = CreatePaletteItem(item.ServerId);
        if (vm != null) SelectedBrush.Items.Add(vm);
        RefreshBrushItemIds();
        SaveToConfig();
    }

    /// <summary>Remove an item from the currently selected brush.</summary>
    [RelayCommand]
    private void RemoveItemFromBrush(PaletteItemViewModel? item)
    {
        if (item == null || SelectedBrush == null) return;
        SelectedBrush.Items.Remove(item);
        RefreshBrushItemIds();
        SaveToConfig();
    }

    /// <summary>When a brush is selected, push its item IDs to the map canvas.</summary>
    partial void OnSelectedBrushChanged(CustomBrushViewModel? value)
    {
        if (value != null && value.Items.Count > 0)
        {
            _parent.BrushServerId = 0; // custom brush overrides single-item
            _parent.BrushItemIds = value.Items.Select(i => i.ServerId).ToList();
        }
        else
        {
            _parent.BrushItemIds = null;
        }
        IsEditingBrush = false;
    }

    /// <summary>Refresh BrushItemIds after items are added/removed from the selected brush.</summary>
    public void RefreshBrushItemIds()
    {
        if (SelectedBrush != null && SelectedBrush.Items.Count > 0)
            _parent.BrushItemIds = SelectedBrush.Items.Select(i => i.ServerId).ToList();
        else
            _parent.BrushItemIds = null;
    }

    // ════════════════════════════════════════════════════════════
    //  Add / Remove items
    // ════════════════════════════════════════════════════════════

    /// <summary>Add a server ID to the currently selected sub-collection.</summary>
    [RelayCommand]
    private void AddItemToSelected(ushort serverId)
    {
        if (SelectedSubCollection == null) return;

        // Avoid duplicates
        if (SelectedSubCollection.Items.Any(i => i.ServerId == serverId))
            return;

        var vm = CreatePaletteItem(serverId);
        if (vm == null) return;

        SelectedSubCollection.Items.Add(vm);
        SearchCatalog();
        SaveToConfig();
    }

    /// <summary>Add a catalog item to a specific sub-collection (from context menu).</summary>
    public void AddItemToSubCollection(PaletteSubCollectionViewModel sub, ushort serverId)
    {
        if (sub.Items.Any(i => i.ServerId == serverId)) return;
        var vm = CreatePaletteItem(serverId);
        if (vm == null) return;
        sub.Items.Add(vm);
        if (SelectedSubCollection == sub)
            SearchCatalog();
        CatalogTabIndex = 1; // switch to Collection tab
        SaveToConfig();
    }

    /// <summary>Add a catalog item to a collection that has no sub-collections — auto-creates a "General" sub.</summary>
    public void AddItemToCollectionRoot(PaletteCollectionViewModel col, ushort serverId)
    {
        // Find or create a default "General" sub-collection
        var sub = col.SubCollections.FirstOrDefault();
        if (sub == null)
        {
            sub = new PaletteSubCollectionViewModel { Name = "General", Parent = col };
            col.SubCollections.Add(sub);
        }
        AddItemToSubCollection(sub, serverId);

        // Auto-navigate to the new sub-collection so the user sees the item
        SelectedCollection = col;
        SelectedSubCollection = sub;
        CatalogTabIndex = 1; // switch to Collection tab
    }

    /// <summary>Add a catalog item to a specific sub-sub-collection (from context menu).</summary>
    public void AddItemToSubSubCollection(PaletteSubSubCollectionViewModel subsub, ushort serverId)
    {
        if (subsub.Items.Any(i => i.ServerId == serverId)) return;
        var vm = CreatePaletteItem(serverId);
        if (vm == null) return;
        subsub.Items.Add(vm);
        if (SelectedSubSubCollection == subsub)
            SearchCatalog();
        CatalogTabIndex = 1; // switch to Collection tab
        SaveToConfig();
    }

    [RelayCommand]
    private void RemoveItemFromSelected(PaletteItemViewModel? item)
    {
        if (item == null || SelectedSubCollection == null) return;
        SelectedSubCollection.Items.Remove(item);
        DisplayedItems.Remove(item);
        SaveToConfig();
    }

    /// <summary>Remove an item from the currently displayed collection view (Collection tab).</summary>
    public void RemoveItemFromCollectionView(PaletteItemViewModel item)
    {
        // Try sub-sub-collection first, then sub-collection
        if (SelectedSubSubCollection != null)
            SelectedSubSubCollection.Items.Remove(item);
        else
            SelectedSubCollection?.Items.Remove(item);
        SaveToConfig();
    }

    /// <summary>Add multiple items to the selected sub-collection (batch).</summary>
    [RelayCommand]
    private void AddCatalogItemToSelected(PaletteItemViewModel? item)
    {
        if (item == null) return;
        AddItemToSelected(item.ServerId);
    }

    // ════════════════════════════════════════════════════════════
    //  Persistence (PaletteConfig)
    // ════════════════════════════════════════════════════════════

    /// <summary>Save after a rename completes (called from code-behind on Enter).</summary>
    public void SaveAfterRename() => SaveToConfig();

    private void SaveToConfig()
    {
        var config = new PaletteConfig
        {
            Collections = Collections
                .Where(c => !c.IsBuiltIn)
                .Select(c => new PaletteCollection
                {
                    Name = c.Name,
                    Icon = c.Icon,
                    SubCollections = c.SubCollections
                        .Where(s => !s.IsBuiltIn)
                        .Select(s => new PaletteSubCollection
                        {
                            Name = s.Name,
                            Items = s.Items.Select(i => i.ServerId).ToList(),
                            SubSubCollections = s.SubSubCollections.Select(ss => new PaletteSubSubCollection
                            {
                                Name = ss.Name,
                                Items = ss.Items.Select(i => i.ServerId).ToList()
                            }).ToList()
                        }).ToList()
                }).ToList(),
            CustomBrushes = CustomBrushes.Select(b => new PaletteCustomBrush
            {
                Name = b.Name,
                Items = b.Items.Select(i => i.ServerId).ToList()
            }).ToList()
        };
        config.Save();
    }

    private void LoadFromConfig()
    {
        var config = PaletteConfig.Load();
        Collections.Clear();

        // 1) Create built-in "Raw" collection with "Others" sub-collection
        var rawCol = new PaletteCollectionViewModel
        {
            Name = "Raw",
            Icon = "fa-solid fa-database",
            IsBuiltIn = true
        };
        var othersSub = new PaletteSubCollectionViewModel
        {
            Name = "Others",
            Parent = rawCol,
            IsBuiltIn = true
        };
        rawCol.SubCollections.Add(othersSub);
        Collections.Add(rawCol);
        RawCollection = rawCol;
        OthersSubCollection = othersSub;

        // 2) Load user-created collections from config
        foreach (var cc in config.Collections)
        {
            var colVm = new PaletteCollectionViewModel
            {
                Name = cc.Name,
                Icon = cc.Icon ?? "fa-solid fa-layer-group"
            };

            foreach (var sc in cc.SubCollections)
            {
                var subVm = new PaletteSubCollectionViewModel
                {
                    Name = sc.Name,
                    Parent = colVm
                };

                foreach (var serverId in sc.Items)
                {
                    var itemVm = CreatePaletteItem(serverId);
                    if (itemVm != null)
                        subVm.Items.Add(itemVm);
                }

                // Load sub-sub-collections (3rd tier)
                foreach (var ssc in sc.SubSubCollections)
                {
                    var subSubVm = new PaletteSubSubCollectionViewModel
                    {
                        Name = ssc.Name,
                        Parent = subVm
                    };

                    foreach (var serverId in ssc.Items)
                    {
                        var itemVm = CreatePaletteItem(serverId);
                        if (itemVm != null)
                            subSubVm.Items.Add(itemVm);
                    }

                    subVm.SubSubCollections.Add(subSubVm);
                }

                colVm.SubCollections.Add(subVm);
            }

            Collections.Add(colVm);
        }

        // Select Raw → Others by default
        SelectedCollection = RawCollection;
        SelectedSubCollection = OthersSubCollection;

        // 3) Load custom brushes
        CustomBrushes.Clear();
        foreach (var cb in config.CustomBrushes)
        {
            var brushVm = new CustomBrushViewModel { Name = cb.Name };
            foreach (var serverId in cb.Items)
            {
                var itemVm = CreatePaletteItem(serverId);
                if (itemVm != null) brushVm.Items.Add(itemVm);
            }
            CustomBrushes.Add(brushVm);
        }
    }

    /// <summary>Clear the sprite bitmap cache (call after sprite mutation).</summary>
    public void ClearSpriteCache() => _spriteCache.Clear();

    /// <summary>The loaded brush catalog (all brush types + tilesets).</summary>
    public BrushCatalog? Catalog { get; private set; }

    /// <summary>
    /// Populate palette collections from the brush catalog tilesets,
    /// matching the OTAcademy map editor's palette structure:
    ///   Primary collections = category types (Terrain, Doodad, Items, Raw)
    ///   Sub-collections = tileset names within each category type
    ///   Dual categories (items_and_raw, etc.) populate multiple collections.
    /// </summary>
    public void LoadFromBrushCatalog(BrushCatalog catalog)
    {
        Catalog = catalog;

        // Map each category type to a primary collection name.
        // Dual types expand to multiple collections (same as OTAcademy).
        static string[] GetCollectionNames(string catType) => catType switch
        {
            "terrain" => ["Terrain"],
            "doodad" => ["Doodad"],
            "raw" => ["Raw"],
            "items" => ["Items"],
            "creatures" => ["Creature"],
            "terrain_and_raw" => ["Terrain", "Raw"],
            "items_and_raw" => ["Items", "Raw"],
            "doodad_and_raw" => ["Doodad", "Raw"],
            "collections" => ["Items"],
            "collections_and_terrain" => ["Items", "Terrain"],
            _ => ["Raw"],
        };

        // Build: collectionName → { tilesetName → entries }
        var collMap = new Dictionary<string, Dictionary<string, List<TilesetEntry>>>();

        foreach (var tileset in catalog.Tilesets)
        {
            foreach (var cat in tileset.Categories)
            {
                foreach (var collName in GetCollectionNames(cat.Type))
                {
                    if (!collMap.TryGetValue(collName, out var tilesetMap))
                    {
                        tilesetMap = new Dictionary<string, List<TilesetEntry>>();
                        collMap[collName] = tilesetMap;
                    }
                    if (!tilesetMap.TryGetValue(tileset.Name, out var entries))
                    {
                        entries = [];
                        tilesetMap[tileset.Name] = entries;
                    }
                    entries.AddRange(cat.Entries);
                }
            }
        }

        // Create collections in stable order
        string[] collOrder = ["Terrain", "Doodad", "Items", "Raw", "Creature"];
        var icons = new Dictionary<string, string>
        {
            ["Terrain"] = "fa-solid fa-mountain-sun",
            ["Doodad"] = "fa-solid fa-tree",
            ["Items"] = "fa-solid fa-box-open",
            ["Raw"] = "fa-solid fa-cube",
            ["Creature"] = "fa-solid fa-dragon",
        };

        foreach (var collName in collOrder)
        {
            if (!collMap.TryGetValue(collName, out var tilesetMap)) continue;

            // For "Raw", add tileset sub-collections to the existing Raw collection
            PaletteCollectionViewModel colVm;
            if (collName == "Raw" && RawCollection != null)
            {
                colVm = RawCollection;
            }
            else
            {
                if (Collections.Any(c => c.Name == collName)) continue;
                colVm = new PaletteCollectionViewModel
                {
                    Name = collName,
                    Icon = icons.GetValueOrDefault(collName, "fa-solid fa-layer-group"),
                    IsBuiltIn = true,
                };
            }

            foreach (var (tilesetName, entryList) in tilesetMap)
            {
                // Skip if this sub-collection already exists (e.g. "Others" in Raw)
                if (colVm.SubCollections.Any(s => s.Name == tilesetName)) continue;

                var subVm = new PaletteSubCollectionViewModel
                {
                    Name = tilesetName,
                    Parent = colVm,
                    IsBuiltIn = true,
                };

                foreach (var entry in entryList)
                {
                    // In Raw, only add item entries (no brushes) — OTAcademy behavior
                    if (collName == "Raw" && entry.Type != "raw") continue;
                    AddEntryToSub(catalog, entry, subVm);
                }

                if (subVm.Items.Count > 0)
                    colVm.SubCollections.Add(subVm);
            }

            // Only add new collections (Raw already exists)
            if (collName != "Raw" || RawCollection == null)
            {
                if (colVm.SubCollections.Count > 0)
                    Collections.Add(colVm);
            }
        }

        // Log diagnostic summary
        int totalItems = 0;
        int withSprite = 0;
        foreach (var col in Collections)
        {
            foreach (var sub in col.SubCollections)
            {
                totalItems += sub.Items.Count;
                withSprite += sub.Items.Count(i => i.Sprite != null);
            }
        }
        _parent.AddMapLog($"Palette: {Collections.Count} collections, {totalItems} items ({withSprite} with sprites)");
    }

    /// <summary>Add a single tileset entry to a sub-collection.
    /// Items are added even if they don't exist in OTB (shown without sprite).</summary>
    private void AddEntryToSub(BrushCatalog catalog, TilesetEntry entry,
        PaletteSubCollectionViewModel sub)
    {
        if (entry.Type == "brush" && entry.BrushName != null)
        {
            var lookId = catalog.GetBrushLookId(entry.BrushName);
            if (lookId > 0)
            {
                _serverToClientMap.TryGetValue(lookId, out var clientId);
                sub.Items.Add(new PaletteItemViewModel
                {
                    ServerId = lookId,
                    ClientId = clientId,
                    Name = entry.BrushName,
                    Article = null,
                    Sprite = GetSprite(lookId),
                });
            }
            else
            {
                // Brush name not found in any brush definition — add placeholder
                sub.Items.Add(new PaletteItemViewModel
                {
                    ServerId = 0,
                    ClientId = 0,
                    Name = entry.BrushName,
                    Article = null,
                    Sprite = null,
                });
            }
        }
        else if (entry.Type == "raw")
        {
            ushort start = entry.ItemId;
            ushort end = entry.ItemIdEnd > 0 ? entry.ItemIdEnd : start;
            for (ushort id = start; id <= end; id++)
            {
                _serverToClientMap.TryGetValue(id, out var clientId);
                sub.Items.Add(new PaletteItemViewModel
                {
                    ServerId = id,
                    ClientId = clientId,
                    Name = entry.DisplayName ?? $"item #{id}",
                    Article = null,
                    Sprite = GetSprite(id),
                });
            }
        }
    }

    /// <summary>Reload sprite thumbnails after client data changes.</summary>
    public void RefreshSprites()
    {
        _spriteCache.Clear();
        foreach (var col in Collections)
        {
            foreach (var sub in col.SubCollections)
            {
                foreach (var item in sub.Items)
                    item.Sprite = GetSprite(item.ServerId);
                foreach (var subSub in sub.SubSubCollections)
                {
                    foreach (var item in subSub.Items)
                        item.Sprite = GetSprite(item.ServerId);
                }
            }
        }
        // Also refresh displayed items
        foreach (var item in DisplayedItems)
            item.Sprite = GetSprite(item.ServerId);
        foreach (var item in CatalogResults)
            item.Sprite = GetSprite(item.ServerId);
    }

    // ════════════════════════════════════════════════════════════
    //  Navigation: Select RAW / Select in Collection
    // ════════════════════════════════════════════════════════════

    /// <summary>
    /// Navigate the catalog to the page containing the given server ID and highlight it.
    /// </summary>
    public void NavigateToCatalogItem(ushort serverId)
    {
        // Navigate to Raw > Others (shows all items)
        SelectedCollection = RawCollection;
        SelectedSubCollection = OthersSubCollection;
        SelectedSubSubCollection = null;
        CatalogSearchText = string.Empty;

        // Find the index in the full sorted list
        int idx = _filteredCatalogIds.IndexOf(serverId);
        if (idx < 0) return;

        // Navigate to the correct page
        int page = idx / CatalogPageSize;
        CatalogPage = page;
        FillCatalogPage();

        // Highlight the item
        HighlightCatalogItem(serverId);
    }

    /// <summary>
    /// Navigate to the sub-collection containing the given server ID.
    /// Returns true if found.
    /// </summary>
    public bool NavigateToSubCollection(ushort serverId)
    {
        foreach (var col in Collections)
        {
            foreach (var sub in col.SubCollections)
            {
                // Check sub-sub-collections first (3rd tier)
                foreach (var subSub in sub.SubSubCollections)
                {
                    if (subSub.Items.Any(i => i.ServerId == serverId))
                    {
                        col.IsExpanded = true;
                        SelectedCollection = col;
                        SelectedSubCollection = sub;
                        SelectedSubSubCollection = subSub;
                        ShowSubCollectionItems(sub);
                        HighlightDisplayedItem(serverId);
                        return true;
                    }
                }

                if (sub.Items.Any(i => i.ServerId == serverId))
                {
                    col.IsExpanded = true;
                    SelectedCollection = col;
                    SelectedSubCollection = sub;
                    SelectedSubSubCollection = null;
                    ShowSubCollectionItems(sub);
                    HighlightDisplayedItem(serverId);
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Clear highlight on all catalog items and set it on the matching one.</summary>
    public void HighlightCatalogItem(ushort serverId)
    {
        foreach (var item in CatalogResults)
            item.IsHighlighted = item.ServerId == serverId;
    }

    /// <summary>Clear highlight on all displayed items and set it on the matching one.</summary>
    private void HighlightDisplayedItem(ushort serverId)
    {
        foreach (var item in DisplayedItems)
            item.IsHighlighted = item.ServerId == serverId;
    }

    /// <summary>
    /// Find the first sub-collection name that contains the given server ID.
    /// Returns "Collection / Sub-Collection" or null if not found.
    /// </summary>
    public string? FindSubCollectionLabel(ushort serverId)
    {
        foreach (var col in Collections)
            foreach (var sub in col.SubCollections)
                if (sub.Items.Any(i => i.ServerId == serverId))
                    return $"{col.Name} / {sub.Name}";
        return null;
    }
}
