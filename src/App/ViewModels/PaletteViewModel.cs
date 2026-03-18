using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using POriginsItemEditor.OTB;

namespace POriginsItemEditor.App.ViewModels;

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

    public string DisplayName => string.IsNullOrEmpty(Article) ? Name : $"{Article} {Name}";
    public string Tooltip => $"[{ServerId}] {DisplayName}";
}

/// <summary>
/// A sub-collection node inside a collection — holds items.
/// </summary>
public partial class PaletteSubCollectionViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isRenaming;
    [ObservableProperty] private string _renameText = string.Empty;

    public ObservableCollection<PaletteItemViewModel> Items { get; } = [];

    public PaletteCollectionViewModel? Parent { get; init; }

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

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

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

    // ── Selected state ──────────────────────────────────────────
    [ObservableProperty] private PaletteCollectionViewModel? _selectedCollection;
    [ObservableProperty] private PaletteSubCollectionViewModel? _selectedSubCollection;
    [ObservableProperty] private PaletteItemViewModel? _selectedPaletteItem;

    // ── Display items for the active sub-collection ─────────────
    public ObservableCollection<PaletteItemViewModel> DisplayedItems { get; } = [];

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
    public bool HasItems => _serverToClientMap.Count > 0;

    // ── Sub-collection search/filter ────────────────────────────
    [ObservableProperty] private string _itemSearchText = string.Empty;

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

    /// <summary>Get a 32×32 sprite thumbnail for a server ID.</summary>
    private WriteableBitmap? GetSprite(ushort serverId)
    {
        if (_spriteCache.TryGetValue(serverId, out var cached))
            return cached;

        WriteableBitmap? bmp = null;
        if (_serverToClientMap.TryGetValue(serverId, out var clientId) && clientId > 0)
        {
            var datData = _parent.ExposedDatData;
            var sprFile = _parent.ExposedSprFile;
            if (datData != null && sprFile != null &&
                datData.Items.TryGetValue(clientId, out var thing) &&
                thing.FrameGroups.Length > 0)
            {
                var fg = thing.FrameGroups[0];
                // Get default sprite: first tile, layer 0, no patterns, frame 0
                uint sprId = fg.GetSpriteId(0, 0, 0, 0, 0, 0, 0);
                if (sprId > 0)
                    bmp = _parent.LoadSpriteBitmap(sprId);
            }
        }

        _spriteCache[serverId] = bmp;
        return bmp;
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
        RefreshDisplayedItems();
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

    /// <summary>Show a sub-collection's items in the south panel.</summary>
    public void ShowSubCollectionItems(PaletteSubCollectionViewModel sub)
    {
        SelectedSubCollection = sub;
        ViewingSubCollectionName = sub.Name;
        IsViewingSubCollection = true;
        RefreshDisplayedItems();
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

    private void SearchCatalog()
    {
        if (_serverToClientMap.Count == 0) { CatalogResults.Clear(); CatalogStatusText = "No items — load OTB + Client"; return; }

        var search = CatalogSearchText.Trim();
        var allIds = _serverToClientMap.Keys;

        if (search.Length == 0)
        {
            _filteredCatalogIds = allIds.OrderBy(id => id).ToList();
        }
        else
        {
            bool startsWithWild = search.StartsWith('*');
            bool endsWithWild = search.EndsWith('*');
            var pattern = search.Trim('*');

            if (pattern.Length == 0)
            {
                _filteredCatalogIds = allIds.OrderBy(id => id).ToList();
            }
            else if (startsWithWild && endsWithWild)
            {
                // *100* → contains
                _filteredCatalogIds = allIds
                    .Where(id => id.ToString().Contains(pattern))
                    .OrderBy(id => id).ToList();
            }
            else if (startsWithWild)
            {
                // *100 → ends with
                _filteredCatalogIds = allIds
                    .Where(id => id.ToString().EndsWith(pattern))
                    .OrderBy(id => id).ToList();
            }
            else if (endsWithWild)
            {
                // 100* → starts with
                _filteredCatalogIds = allIds
                    .Where(id => id.ToString().StartsWith(pattern))
                    .OrderBy(id => id).ToList();
            }
            else
            {
                // Exact match
                if (ushort.TryParse(pattern, out var exactId) && allIds.Contains(exactId))
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
        var page = _filteredCatalogIds
            .Skip(CatalogPage * CatalogPageSize)
            .Take(CatalogPageSize);

        foreach (var serverId in page)
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
    private void RemoveCollection(PaletteCollectionViewModel? col)
    {
        if (col == null) return;
        Collections.Remove(col);
        if (SelectedCollection == col) SelectedCollection = Collections.FirstOrDefault();
        SaveToConfig();
    }

    [RelayCommand]
    private void AddSubCollection()
    {
        var col = SelectedCollection;
        if (col == null) return;

        var sub = new PaletteSubCollectionViewModel { Name = "New Sub-Collection", Parent = col };
        col.SubCollections.Add(sub);
        col.IsExpanded = true;
        SelectedSubCollection = sub;
        sub.BeginRenameCommand.Execute(null);
        SaveToConfig();
    }

    [RelayCommand]
    private void RemoveSubCollection(PaletteSubCollectionViewModel? sub)
    {
        if (sub?.Parent == null) return;
        sub.Parent.SubCollections.Remove(sub);
        if (SelectedSubCollection == sub)
            SelectedSubCollection = sub.Parent.SubCollections.FirstOrDefault();
        SaveToConfig();
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
        RefreshDisplayedItems();
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
            RefreshDisplayedItems();
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
            Collections = Collections.Select(c => new PaletteCollection
            {
                Name = c.Name,
                Icon = c.Icon,
                SubCollections = c.SubCollections.Select(s => new PaletteSubCollection
                {
                    Name = s.Name,
                    Items = s.Items.Select(i => i.ServerId).ToList()
                }).ToList()
            }).ToList()
        };
        config.Save();
    }

    private void LoadFromConfig()
    {
        var config = PaletteConfig.Load();
        Collections.Clear();

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

                colVm.SubCollections.Add(subVm);
            }

            Collections.Add(colVm);
        }

        // Select first collection/subcollection if available
        SelectedCollection = Collections.FirstOrDefault();
        SelectedSubCollection = SelectedCollection?.SubCollections.FirstOrDefault();
    }

    /// <summary>Clear the sprite bitmap cache (call after sprite mutation).</summary>
    public void ClearSpriteCache() => _spriteCache.Clear();

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
        // Ensure we're in catalog view
        IsViewingSubCollection = false;
        CatalogSearchText = string.Empty; // reset filter to show all items

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
                if (sub.Items.Any(i => i.ServerId == serverId))
                {
                    // Expand the collection and select the sub-collection
                    col.IsExpanded = true;
                    SelectedCollection = col;
                    SelectedSubCollection = sub;
                    ShowSubCollectionItems(sub);

                    // Highlight the item
                    HighlightDisplayedItem(serverId);
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Clear highlight on all catalog items and set it on the matching one.</summary>
    private void HighlightCatalogItem(ushort serverId)
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
