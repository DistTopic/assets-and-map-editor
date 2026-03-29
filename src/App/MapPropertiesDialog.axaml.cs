using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AssetsAndMapEditor.App;

public sealed class MapPropertiesResult
{
    public required string Description { get; init; }
    public required ushort Width { get; init; }
    public required ushort Height { get; init; }
    public required string HouseFile { get; init; }
    public required string SpawnFile { get; init; }
}

public partial class MapPropertiesDialog : Window
{
    public MapPropertiesResult? Result { get; private set; }

    public MapPropertiesDialog()
    {
        InitializeComponent();
    }

    public string MapDescription
    {
        set => DescriptionBox.Text = value;
    }

    public ushort MapWidth
    {
        set => WidthSpin.Value = value;
    }

    public ushort MapHeight
    {
        set => HeightSpin.Value = value;
    }

    public string MapHouseFile
    {
        set => HouseFileBox.Text = value;
    }

    public string MapSpawnFile
    {
        set => SpawnFileBox.Text = value;
    }

    public string OtbmVersion
    {
        set => OtbmVersionText.Text = value;
    }

    public string OtbVersion
    {
        set => OtbVersionText.Text = value;
    }

    public int TileCount
    {
        set => TileCountText.Text = value.ToString("N0");
    }

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        var w = (ushort)Math.Clamp(WidthSpin.Value ?? 256, 64, 65000);
        var h = (ushort)Math.Clamp(HeightSpin.Value ?? 256, 64, 65000);

        Result = new MapPropertiesResult
        {
            Description = DescriptionBox.Text ?? string.Empty,
            Width = w,
            Height = h,
            HouseFile = HouseFileBox.Text?.Trim() ?? string.Empty,
            SpawnFile = SpawnFileBox.Text?.Trim() ?? string.Empty,
        };

        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }
}
