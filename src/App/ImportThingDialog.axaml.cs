using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using AssetsAndMapEditor.OTB;

namespace AssetsAndMapEditor.App;

public sealed class ImportThingResult
{
    public required DatThingType Thing { get; init; }
    public required Dictionary<uint, byte[]> SpriteData { get; init; }
    public required ThingCategory Category { get; init; }
}

public partial class ImportThingDialog : Window
{
    public ImportThingResult? Result { get; private set; }

    // OBD mode state
    private ObdCodec.ObdData? _obdData;
    private List<string>? _obdFiles;

    // Folder mode state
    private DatData? _sourceDat;
    private SprFile? _sourceSpr;
    private DatThingType? _previewThing;

    private bool IsObdMode => ObdModeRadio?.IsChecked == true;

    public ImportThingDialog()
    {
        InitializeComponent();
    }

    // ── Mode switching ──

    private void ImportMode_Changed(object? sender, RoutedEventArgs e)
    {
        if (ObdPanel is null || FolderPanel is null) return;
        ObdPanel.IsVisible = IsObdMode;
        FolderPanel.IsVisible = !IsObdMode;
        ClearPreview();
    }

    // ── OBD file browse ──

    private async void BrowseObd_Click(object? sender, RoutedEventArgs e)
    {
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select OBD File(s)",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Object Builder Data") { Patterns = ["*.obd"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] },
            ],
        });

        if (result.Count == 0) return;

        _obdFiles = [];
        foreach (var file in result)
        {
            var path = file.TryGetLocalPath();
            if (path != null) _obdFiles.Add(path);
        }

        if (_obdFiles.Count == 0) return;

        // Load first file for preview
        try
        {
            var bytes = File.ReadAllBytes(_obdFiles[0]);
            _obdData = ObdCodec.Decode(bytes);

            ObdFileBox.Text = _obdFiles.Count == 1
                ? Path.GetFileName(_obdFiles[0])
                : $"{_obdFiles.Count} files selected";

            var thing = _obdData.Thing;
            SourceInfoLabel.Text = $"OBD v{_obdData.ObdVersion} — client {_obdData.ClientVersion} — " +
                                    $"{_obdData.Category} with {_obdData.Sprites.Count} sprite(s)";
            SourceInfoLabel.Foreground = Avalonia.Media.Brush.Parse("#a6e3a1");

            UpdateObdPreview();
            HideError();
        }
        catch (Exception ex)
        {
            ShowError($"Failed to read OBD: {ex.Message}");
        }
    }

    private void UpdateObdPreview()
    {
        if (_obdData == null) return;

        var thing = _obdData.Thing;
        var fg = thing.FrameGroups.Length > 0 ? thing.FrameGroups[0] : null;
        if (fg != null)
        {
            PreviewInfoLabel.Text = $"{_obdData.Category} — {fg.Width}×{fg.Height}, " +
                                     $"{fg.Layers}L, {fg.PatternX}×{fg.PatternY}×{fg.PatternZ} pat, {fg.Frames}F";
            PreviewSpriteLabel.Text = $"{_obdData.Sprites.Count} sprite(s)";
            PreviewImage.Source = ComposeObdPreview(thing, _obdData.Sprites);
        }
        else
        {
            PreviewInfoLabel.Text = $"{_obdData.Category}: no frame groups";
        }

        ImportButton.IsEnabled = true;
    }

    private WriteableBitmap? ComposeObdPreview(DatThingType thing, Dictionary<uint, byte[]> sprites)
    {
        if (thing.FrameGroups.Length == 0) return null;
        var fg = thing.FrameGroups[0];
        int w = fg.Width, h = fg.Height;
        if (w == 0 || h == 0) return null;

        int bmpW = w * 32, bmpH = h * 32;
        var pixels = new byte[bmpW * bmpH * 4];

        int patX = thing.Category == ThingCategory.Outfit && fg.PatternX > 2 ? 2 : 0;

        for (int tw = 0; tw < w; tw++)
        for (int th = 0; th < h; th++)
        {
            uint sprId = fg.GetSpriteId(tw, th, 0, patX, 0, 0, 0);
            if (sprId == 0 || !sprites.TryGetValue(sprId, out var rgba)) continue;

            int destX = (w - 1 - tw) * 32, destY = (h - 1 - th) * 32;
            BlitSprite(pixels, bmpW, destX, destY, rgba);
        }

        return CreateBitmap(pixels, bmpW, bmpH);
    }

    // ── Folder browse ──

    private async void BrowseSource_Click(object? sender, RoutedEventArgs e)
    {
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Source Client Folder",
            AllowMultiple = false,
        });

        if (result.Count == 0) return;
        var folderPath = result[0].TryGetLocalPath();
        if (folderPath != null) await LoadSourceFolder(folderPath);
    }

    private async Task LoadSourceFolder(string folderPath)
    {
        _sourceSpr?.Dispose();
        _sourceSpr = null;
        _sourceDat = null;

        var (datPath, sprPath) = FindClientFiles(folderPath);
        if (datPath == null || sprPath == null)
        {
            ShowError("Tibia.dat and Tibia.spr not found.");
            return;
        }

        try
        {
            _sourceDat = DatFile.Load(datPath);
            _sourceSpr = SprFile.Load(sprPath, _sourceDat.Extended);
            SourceFolderBox.Text = folderPath;
            SourceInfoLabel.Text = $"v{_sourceDat.ProtocolVersion} — {_sourceDat.Items.Count} items, " +
                                    $"{_sourceDat.Outfits.Count} outfits, {_sourceDat.Effects.Count} effects, " +
                                    $"{_sourceDat.Missiles.Count} missiles";
            SourceInfoLabel.Foreground = Avalonia.Media.Brush.Parse("#a6e3a1");
            HideError();
            UpdateFolderPreview();
        }
        catch (Exception ex)
        {
            ShowError($"Failed to load: {ex.Message}");
        }
    }

    private void CategoryOrId_Changed(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (ObdModeRadio is null) return;
        if (!IsObdMode) UpdateFolderPreview();
    }

    private void ThingIdBox_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        if (!IsObdMode) UpdateFolderPreview();
    }

    private void UpdateFolderPreview()
    {
        _previewThing = null;
        ImportButton.IsEnabled = false;
        PreviewImage.Source = null;
        PreviewSpriteLabel.Text = "";

        if (_sourceDat == null || _sourceSpr == null)
        {
            PreviewInfoLabel.Text = "Select a source folder first";
            return;
        }

        if (!ushort.TryParse(ThingIdBox.Text?.Trim(), out var id) || id == 0)
        {
            PreviewInfoLabel.Text = "Enter a valid thing ID";
            return;
        }

        var category = GetSelectedCategory();
        var dict = GetDictForCategory(_sourceDat, category);

        if (!dict.TryGetValue(id, out var thing))
        {
            PreviewInfoLabel.Text = $"No {category} with ID {id} in source";
            return;
        }

        _previewThing = thing;

        var fg = thing.FrameGroups.Length > 0 ? thing.FrameGroups[0] : null;
        if (fg != null)
        {
            PreviewInfoLabel.Text = $"{thing.Category} #{thing.Id}: {fg.Width}×{fg.Height}, " +
                                     $"{fg.Layers}L, {fg.PatternX}×{fg.PatternY}×{fg.PatternZ} pat, {fg.Frames}F";
            PreviewSpriteLabel.Text = $"{thing.TotalSpriteCount} sprite(s)";
            PreviewImage.Source = ComposeFolderPreview(thing);
        }
        else
        {
            PreviewInfoLabel.Text = $"{thing.Category} #{thing.Id}: no frame groups";
        }

        ImportButton.IsEnabled = true;
        HideError();
    }

    private WriteableBitmap? ComposeFolderPreview(DatThingType thing)
    {
        if (_sourceSpr == null || thing.FrameGroups.Length == 0) return null;
        var fg = thing.FrameGroups[0];
        int w = fg.Width, h = fg.Height;
        if (w == 0 || h == 0) return null;

        int bmpW = w * 32, bmpH = h * 32;
        var pixels = new byte[bmpW * bmpH * 4];

        int patX = thing.Category == ThingCategory.Outfit && fg.PatternX > 2 ? 2 : 0;

        for (int tw = 0; tw < w; tw++)
        for (int th = 0; th < h; th++)
        {
            uint sprId = fg.GetSpriteId(tw, th, 0, patX, 0, 0, 0);
            var rgba = _sourceSpr.GetSpriteRgba(sprId);
            if (rgba == null) continue;
            int destX = (w - 1 - tw) * 32, destY = (h - 1 - th) * 32;
            BlitSprite(pixels, bmpW, destX, destY, rgba);
        }

        return CreateBitmap(pixels, bmpW, bmpH);
    }

    // ── Import button ──

    private void ImportButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (IsObdMode)
                ImportFromObd();
            else
                ImportFromFolder();
        }
        catch (Exception ex)
        {
            ShowError($"Import failed: {ex.Message}");
        }
    }

    private void ImportFromObd()
    {
        if (_obdData == null) return;

        Result = new ImportThingResult
        {
            Thing = _obdData.Thing.Clone(),
            SpriteData = new Dictionary<uint, byte[]>(_obdData.Sprites),
            Category = _obdData.Category,
        };

        Close();
    }

    private void ImportFromFolder()
    {
        if (_previewThing == null || _sourceSpr == null) return;

        var spriteData = new Dictionary<uint, byte[]>();
        foreach (var fg in _previewThing.FrameGroups)
        {
            if (fg.SpriteIndex == null) continue;
            foreach (var sprId in fg.SpriteIndex)
            {
                if (sprId == 0 || spriteData.ContainsKey(sprId)) continue;
                var rgba = _sourceSpr.GetSpriteRgba(sprId);
                if (rgba != null)
                    spriteData[sprId] = rgba;
            }
        }

        Result = new ImportThingResult
        {
            Thing = _previewThing.Clone(),
            SpriteData = spriteData,
            Category = GetSelectedCategory(),
        };

        _sourceSpr?.Dispose();
        Close();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Result = null;
        _sourceSpr?.Dispose();
        Close();
    }

    // ── Helpers ──

    private void ClearPreview()
    {
        ImportButton.IsEnabled = false;
        PreviewImage.Source = null;
        PreviewInfoLabel.Text = "Select a file or source to preview";
        PreviewSpriteLabel.Text = "";
        SourceInfoLabel.Text = IsObdMode ? "No file selected" : "No source loaded";
        SourceInfoLabel.Foreground = Avalonia.Media.Brush.Parse("#585b70");
    }

    private ThingCategory GetSelectedCategory()
    {
        return CategoryBox.SelectedIndex switch
        {
            1 => ThingCategory.Outfit,
            2 => ThingCategory.Effect,
            3 => ThingCategory.Missile,
            _ => ThingCategory.Item,
        };
    }

    private static Dictionary<ushort, DatThingType> GetDictForCategory(DatData dat, ThingCategory category)
    {
        return category switch
        {
            ThingCategory.Outfit => dat.Outfits,
            ThingCategory.Effect => dat.Effects,
            ThingCategory.Missile => dat.Missiles,
            _ => dat.Items,
        };
    }

    private static void BlitSprite(byte[] pixels, int bmpW, int destX, int destY, byte[] rgba)
    {
        for (int y = 0; y < 32; y++)
        for (int x = 0; x < 32; x++)
        {
            int srcIdx = (y * 32 + x) * 4;
            if (srcIdx + 3 >= rgba.Length) continue;
            byte a = rgba[srcIdx + 3];
            if (a == 0) continue;
            int dstIdx = ((destY + y) * bmpW + destX + x) * 4;
            pixels[dstIdx] = rgba[srcIdx];
            pixels[dstIdx + 1] = rgba[srcIdx + 1];
            pixels[dstIdx + 2] = rgba[srcIdx + 2];
            pixels[dstIdx + 3] = a;
        }
    }

    private static WriteableBitmap? CreateBitmap(byte[] pixels, int w, int h)
    {
        try
        {
            var bmp = new WriteableBitmap(
                new PixelSize(w, h),
                new Vector(96, 96),
                PixelFormat.Rgba8888,
                AlphaFormat.Unpremul);
            using (var fb = bmp.Lock())
                Marshal.Copy(pixels, 0, fb.Address, pixels.Length);
            return bmp;
        }
        catch { return null; }
    }

    private static (string? datPath, string? sprPath) FindClientFiles(string folder)
    {
        string[] searchPaths = [folder, Path.Combine(folder, "data", "things")];
        foreach (var basePath in searchPaths)
        {
            if (!Directory.Exists(basePath)) continue;
            var dat = Path.Combine(basePath, "Tibia.dat");
            var spr = Path.Combine(basePath, "Tibia.spr");
            if (File.Exists(dat) && File.Exists(spr)) return (dat, spr);
            foreach (var subDir in Directory.GetDirectories(basePath))
            {
                dat = Path.Combine(subDir, "Tibia.dat");
                spr = Path.Combine(subDir, "Tibia.spr");
                if (File.Exists(dat) && File.Exists(spr)) return (dat, spr);
            }
        }
        return (null, null);
    }

    private void ShowError(string msg)
    {
        ErrorLabel.Text = msg;
        ErrorLabel.IsVisible = true;
    }

    private void HideError()
    {
        ErrorLabel.IsVisible = false;
    }
}
