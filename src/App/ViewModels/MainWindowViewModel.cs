using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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

    // ── Dropdown options for ComboBoxes ──
    public OtbGroup[] GroupOptions { get; } = [OtbGroup.None, OtbGroup.Ground, OtbGroup.Container, OtbGroup.Splash, OtbGroup.Fluid, OtbGroup.Deprecated];
    public string[] StackOrderOptions { get; } = ["None", "Border", "Bottom", "Top"];
    public MinimapColorEntry[] MinimapPalette => TibiaColors.Palette;

    public ObservableCollection<ItemViewModel> Items { get; } = [];

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnShowMismatchesOnlyChanged(bool value) => ApplyFilter();
    partial void OnShowDeprecatedOnlyChanged(bool value) => ApplyFilter();

    partial void OnSelectedItemChanged(ItemViewModel? value)
    {
    }

    [RelayCommand]
    private async Task SelectClientFolderAsync()
    {
        var folderPath = await FileDialogHelper.OpenFolderAsync("Selecionar pasta do Client");
        if (folderPath == null) return;

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

        try
        {
            _otbData = OtbFile.Load(path);
            _otbPath = path;
            BuildItemList();
            StatusText = $"OTB carregado: {_otbData.Items.Count} itens — {Path.GetFileName(path)}";
            HasUnsavedChanges = false;
        }
        catch (Exception ex)
        {
            StatusText = $"Erro ao carregar OTB: {ex.Message}";
        }
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
            if (_datData != null && _datData.Items.TryGetValue(vm.ClientId, out var datInfo))
            {
                vm.DatAnimPhases = datInfo.AnimPhases;
                vm.DatAnimateAlways = datInfo.AnimateAlways;
                vm.FirstSpriteId = datInfo.FirstSpriteId;
                vm.HasMismatch = (datInfo.AnimPhases > 1) != vm.IsAnimation;
            }
            else
            {
                vm.DatAnimPhases = 0;
                vm.DatAnimateAlways = false;
                vm.FirstSpriteId = 0;
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
            LoadSpriteForItem(vm);
        }
    }

    private void LoadSpriteForItem(ItemViewModel item)
    {
        if (_sprFile == null || item.FirstSpriteId == 0)
        {
            item.Sprite = null;
            return;
        }

        try
        {
            var rgba = _sprFile.GetSpriteRgba(item.FirstSpriteId);
            if (rgba == null)
            {
                item.Sprite = null;
                return;
            }

            var bitmap = new WriteableBitmap(
                new PixelSize(32, 32),
                new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Rgba8888,
                Avalonia.Platform.AlphaFormat.Unpremul);

            using (var fb = bitmap.Lock())
            {
                Marshal.Copy(rgba, 0, fb.Address, rgba.Length);
            }

            item.Sprite = bitmap;
        }
        catch
        {
            item.Sprite = null;
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
    }
}
