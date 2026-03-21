using System.Buffers.Binary;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using POriginsItemEditor.OTB;

namespace POriginsItemEditor.App;

/// <summary>
/// Result returned by the OpenClientDialog when the user clicks Load.
/// </summary>
public sealed class OpenClientResult
{
    public required string DatPath { get; init; }
    public required string SprPath { get; init; }
    public required string FolderPath { get; init; }
    public required int ProtocolVersion { get; init; }
    public required bool Extended { get; init; }
    public required bool Transparency { get; init; }
    public required bool ImprovedAnimations { get; init; }
    public required bool FrameGroups { get; init; }
}

public partial class OpenClientDialog : Window
{
    private string? _datPath;
    private string? _sprPath;
    private int _detectedProtocol;
    private bool _detectedExtended;

    public OpenClientResult? Result { get; private set; }

    public OpenClientDialog()
    {
        InitializeComponent();
    }

    private async void BrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Client Folder",
            AllowMultiple = false,
        });

        if (result.Count == 0) return;
        var folderPath = result[0].TryGetLocalPath();
        if (folderPath == null) return;

        FolderPathBox.Text = folderPath;
        ScanFolder(folderPath);
    }

    private void ScanFolder(string folderPath)
    {
        ResetInfo();

        // Find .dat and .spr files
        var (datPath, sprPath) = FindClientFiles(folderPath);
        if (datPath == null || sprPath == null)
        {
            SetError("Tibia.dat / Tibia.spr not found in the selected folder.");
            return;
        }

        try
        {
            // Read DAT header (first 12 bytes: signature + 4 counts)
            var datHeader = new byte[12];
            using (var fs = new FileStream(datPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (fs.Read(datHeader, 0, 12) < 12)
                {
                    SetError("DAT file is too small.");
                    return;
                }
            }

            var datSignature = BinaryPrimitives.ReadUInt32LittleEndian(datHeader.AsSpan(0));
            var lastItemId = BinaryPrimitives.ReadUInt16LittleEndian(datHeader.AsSpan(4));
            var lastOutfitId = BinaryPrimitives.ReadUInt16LittleEndian(datHeader.AsSpan(6));
            var lastEffectId = BinaryPrimitives.ReadUInt16LittleEndian(datHeader.AsSpan(8));
            var lastMissileId = BinaryPrimitives.ReadUInt16LittleEndian(datHeader.AsSpan(10));

            // Detect protocol — try full parse to find the actual working protocol
            int detectedFromSig = DatFile.DetectProtocol(datSignature);
            bool detectedExtended = detectedFromSig >= 960;
            try
            {
                var testData = DatFile.Load(datPath);
                _detectedProtocol = testData.ProtocolVersion;
                _detectedExtended = testData.Extended;
            }
            catch
            {
                _detectedProtocol = detectedFromSig;
                _detectedExtended = detectedExtended;
            }

            // Read SPR header — extended uses 8-byte header (U32 count), legacy uses 6-byte (U16 count)
            var sprHeaderSize = _detectedExtended ? 8 : 6;
            var sprHeader = new byte[sprHeaderSize];
            using (var fs = new FileStream(sprPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (fs.Read(sprHeader, 0, sprHeaderSize) < sprHeaderSize)
                {
                    SetError("SPR file is too small.");
                    return;
                }
            }

            var sprSignature = BinaryPrimitives.ReadUInt32LittleEndian(sprHeader.AsSpan(0));
            uint spriteCount = _detectedExtended
                ? BinaryPrimitives.ReadUInt32LittleEndian(sprHeader.AsSpan(4))
                : BinaryPrimitives.ReadUInt16LittleEndian(sprHeader.AsSpan(4));

            _datPath = datPath;
            _sprPath = sprPath;

            // Display info
            var versionNote = detectedFromSig != _detectedProtocol
                ? $"{_detectedProtocol} (auto-detected)"
                : $"{_detectedProtocol}";
            if (_detectedExtended && _detectedProtocol < 960)
                versionNote += " [extended sprites]";
            VersionLabel.Text = versionNote;
            DatSignatureLabel.Text = datSignature.ToString("X8");
            ItemsCountLabel.Text = (lastItemId - 100 + 1).ToString();
            OutfitsCountLabel.Text = lastOutfitId.ToString();
            EffectsCountLabel.Text = lastEffectId.ToString();
            MissilesCountLabel.Text = lastMissileId.ToString();
            SprSignatureLabel.Text = sprSignature.ToString("X8");
            SpritesCountLabel.Text = spriteCount.ToString();

            // Auto-set features based on detected format
            ExtendedCheckBox.IsChecked = _detectedExtended;
            ExtendedCheckBox.IsEnabled = !_detectedExtended;

            TransparencyCheckBox.IsEnabled = true;
            TransparencyCheckBox.IsChecked = false;

            ImprovedAnimationsCheckBox.IsChecked = _detectedProtocol >= 1050;
            ImprovedAnimationsCheckBox.IsEnabled = _detectedProtocol < 1050;

            FrameGroupsCheckBox.IsChecked = _detectedProtocol >= 1057;
            FrameGroupsCheckBox.IsEnabled = true;

            LoadButton.IsEnabled = true;
            ClearError();
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    private void Feature_Changed(object? sender, RoutedEventArgs e)
    {
        // Features changed — just allow user override
    }

    private void LoadButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_datPath == null || _sprPath == null) return;

        Result = new OpenClientResult
        {
            DatPath = _datPath,
            SprPath = _sprPath,
            FolderPath = FolderPathBox.Text ?? Path.GetDirectoryName(_datPath) ?? "",
            ProtocolVersion = _detectedProtocol,
            Extended = ExtendedCheckBox.IsChecked == true,
            Transparency = TransparencyCheckBox.IsChecked == true,
            ImprovedAnimations = ImprovedAnimationsCheckBox.IsChecked == true,
            FrameGroups = FrameGroupsCheckBox.IsChecked == true,
        };

        Close();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }

    private void ResetInfo()
    {
        _datPath = null;
        _sprPath = null;
        _detectedProtocol = 0;

        VersionLabel.Text = "";
        DatSignatureLabel.Text = "";
        ItemsCountLabel.Text = "";
        OutfitsCountLabel.Text = "";
        EffectsCountLabel.Text = "";
        MissilesCountLabel.Text = "";
        SprSignatureLabel.Text = "";
        SpritesCountLabel.Text = "";

        ExtendedCheckBox.IsChecked = false;
        ExtendedCheckBox.IsEnabled = false;
        TransparencyCheckBox.IsChecked = false;
        TransparencyCheckBox.IsEnabled = false;
        ImprovedAnimationsCheckBox.IsChecked = false;
        ImprovedAnimationsCheckBox.IsEnabled = false;
        FrameGroupsCheckBox.IsChecked = false;
        FrameGroupsCheckBox.IsEnabled = false;

        LoadButton.IsEnabled = false;
        ClearError();
    }

    private void SetError(string text)
    {
        ErrorIcon.IsVisible = true;
        ErrorLabel.IsVisible = true;
        ErrorLabel.Text = text;
        LoadButton.IsEnabled = false;
    }

    private void ClearError()
    {
        ErrorIcon.IsVisible = false;
        ErrorLabel.IsVisible = false;
        ErrorLabel.Text = "";
    }

    private static (string? datPath, string? sprPath) FindClientFiles(string folder)
    {
        string[] searchPaths = [folder, Path.Combine(folder, "data", "things")];

        foreach (var basePath in searchPaths)
        {
            if (!Directory.Exists(basePath)) continue;

            var dat = Path.Combine(basePath, "Tibia.dat");
            var spr = Path.Combine(basePath, "Tibia.spr");
            if (File.Exists(dat) && File.Exists(spr))
                return (dat, spr);

            foreach (var subDir in Directory.GetDirectories(basePath))
            {
                dat = Path.Combine(subDir, "Tibia.dat");
                spr = Path.Combine(subDir, "Tibia.spr");
                if (File.Exists(dat) && File.Exists(spr))
                    return (dat, spr);
            }
        }

        // Fallback: find any .dat/.spr pair
        foreach (var basePath in searchPaths)
        {
            if (!Directory.Exists(basePath)) continue;
            string? foundDat = null, foundSpr = null;
            foreach (var file in Directory.GetFiles(basePath))
            {
                if (foundDat == null && file.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
                    foundDat = file;
                if (foundSpr == null && file.EndsWith(".spr", StringComparison.OrdinalIgnoreCase))
                    foundSpr = file;
                if (foundDat != null && foundSpr != null)
                    return (foundDat, foundSpr);
            }
        }

        return (null, null);
    }
}
