using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using POriginsItemEditor.OTB;

namespace POriginsItemEditor.App;

public enum ExportFormat { Png, Bmp, Jpg, Obd }

public sealed class ExportResult
{
    public required string OutputFolder { get; init; }
    public required string FileName { get; init; }
    public required ExportFormat Format { get; init; }
    public required bool TransparentBackground { get; init; }
    public required int JpegQuality { get; init; }
    public ushort ObdVersion { get; init; } = ObdCodec.OBD_V2;
    public ushort ClientVersion { get; init; } = 854;
}

public partial class ExportDialog : Window
{
    public ExportResult? Result { get; private set; }

    private static readonly (string Label, ushort Value)[] ObdVersions =
    [
        ("Version 1 (v1)", ObdCodec.OBD_V1),
        ("Version 2 (v2)", ObdCodec.OBD_V2),
        ("Version 3 (v3)", ObdCodec.OBD_V3),
    ];

    private static readonly ushort[] ClientVersionList =
    [
        710, 740, 750, 760, 770, 772, 780, 790, 792,
        800, 810, 811, 820, 830, 840, 842, 850, 853, 854,
        860, 861, 862, 870, 871, 872,
        900, 910, 920, 940, 941, 942, 943, 944,
        950, 951, 952, 953, 954, 960, 961, 962, 963,
        970, 971, 972, 973, 974, 975, 976, 977, 978, 979, 980,
        981, 982, 983, 984, 985, 986,
        1000, 1001, 1002, 1010, 1011, 1012, 1013,
        1020, 1021, 1030, 1031, 1032, 1033, 1034, 1035,
        1036, 1037, 1038, 1039, 1040, 1041, 1050, 1051,
        1052, 1053, 1054, 1055, 1056, 1057, 1058, 1059,
        1060, 1061, 1062, 1063, 1064,
        1070, 1071, 1072, 1073, 1074, 1075, 1076, 1077, 1078, 1079, 1080,
        1081, 1082, 1090, 1091, 1092, 1093, 1094, 1095, 1096, 1097, 1098, 1099,
        1100,
    ];

    /// <summary>When true, hides the OBD format option (sprites are images only).</summary>
    public bool SpriteOnly
    {
        set
        {
            if (!value) return;
            FormatObd.IsVisible = false;
            FormatPng.IsChecked = true;
            // Trigger format change to show image options
            Format_Changed(null, null!);
        }
    }

    /// <summary>Pre-fill the filename (e.g. "item_100", "sprite_1234").</summary>
    public string SuggestedFileName
    {
        set => FileNameBox.Text = value;
    }

    /// <summary>Pre-fill the output folder.</summary>
    public string SuggestedFolder
    {
        set
        {
            OutputFolderBox.Text = value;
            UpdateExportEnabled();
        }
    }

    /// <summary>Auto-select the client version that matches the loaded session.</summary>
    public ushort SuggestedClientVersion
    {
        set
        {
            for (int i = 0; i < ClientVersionList.Length; i++)
            {
                if (ClientVersionList[i] == value)
                {
                    ClientVersionCombo.SelectedIndex = i;
                    return;
                }
            }
            // If exact match not found, add it and select it
            ClientVersionCombo.Items.Add(value.ToString());
            ClientVersionCombo.SelectedIndex = ClientVersionCombo.Items.Count - 1;
        }
    }

    public ExportDialog()
    {
        InitializeComponent();
        PopulateObdCombos();
    }

    private void PopulateObdCombos()
    {
        foreach (var (label, _) in ObdVersions)
            ObdVersionCombo.Items.Add(label);
        ObdVersionCombo.SelectedIndex = 1; // default V2

        foreach (var ver in ClientVersionList)
            ClientVersionCombo.Items.Add(ver.ToString());
        // default to 854
        var idx854 = Array.IndexOf(ClientVersionList, (ushort)854);
        ClientVersionCombo.SelectedIndex = idx854 >= 0 ? idx854 : 0;
    }

    private async void BrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Output Folder",
            AllowMultiple = false,
        });

        if (result.Count == 0) return;
        var folderPath = result[0].TryGetLocalPath();
        if (folderPath == null) return;

        OutputFolderBox.Text = folderPath;
        UpdateExportEnabled();
    }

    private void Format_Changed(object? sender, RoutedEventArgs e)
    {
        // Guard: may fire during InitializeComponent before all x:Name fields are assigned
        if (ImageOptionsPanel is null || ObdOptionsPanel is null) return;

        var format = GetSelectedFormat();
        bool isImage = format != ExportFormat.Obd;

        // Transparent background only for PNG
        TransparentCheckBox.IsVisible = format == ExportFormat.Png;

        // Quality slider only for JPG
        QualityPanel.IsVisible = format == ExportFormat.Jpg;

        // Toggle panels
        ImageOptionsPanel.IsVisible = isImage;
        ObdOptionsPanel.IsVisible = !isImage;
    }

    private void Input_Changed(object? sender, TextChangedEventArgs e)
    {
        UpdateExportEnabled();
    }

    private void QualitySlider_Changed(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Slider.ValueProperty && QualityLabel != null)
            QualityLabel.Text = $"{(int)QualitySlider.Value}%";
    }

    private void ExportButton_Click(object? sender, RoutedEventArgs e)
    {
        var folder = OutputFolderBox.Text?.Trim();
        var name = FileNameBox.Text?.Trim();
        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(name))
            return;

        // Resolve OBD version from combo
        var obdVer = ObdVersionCombo.SelectedIndex >= 0 && ObdVersionCombo.SelectedIndex < ObdVersions.Length
            ? ObdVersions[ObdVersionCombo.SelectedIndex].Value
            : ObdCodec.OBD_V2;

        // Resolve client version from combo
        ushort clientVer = 854;
        if (ClientVersionCombo.SelectedItem is string cvStr && ushort.TryParse(cvStr, out var parsed))
            clientVer = parsed;

        Result = new ExportResult
        {
            OutputFolder = folder,
            FileName = name,
            Format = GetSelectedFormat(),
            TransparentBackground = TransparentCheckBox.IsChecked == true,
            JpegQuality = (int)QualitySlider.Value,
            ObdVersion = obdVer,
            ClientVersion = clientVer,
        };

        Close();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }

    private ExportFormat GetSelectedFormat()
    {
        if (FormatObd.IsChecked == true) return ExportFormat.Obd;
        if (FormatBmp.IsChecked == true) return ExportFormat.Bmp;
        if (FormatJpg.IsChecked == true) return ExportFormat.Jpg;
        return ExportFormat.Png;
    }

    private void UpdateExportEnabled()
    {
        var hasFolder = !string.IsNullOrWhiteSpace(OutputFolderBox.Text);
        var hasName = !string.IsNullOrWhiteSpace(FileNameBox.Text);
        ExportButton.IsEnabled = hasFolder && hasName;

        if (!hasFolder || !hasName)
        {
            ErrorIcon.IsVisible = false;
            ErrorLabel.IsVisible = false;
        }
    }
}
