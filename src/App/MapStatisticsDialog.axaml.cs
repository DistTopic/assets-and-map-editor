using Avalonia.Controls;
using Avalonia.Interactivity;
using AssetsAndMapEditor.OTB;

namespace AssetsAndMapEditor.App;

public partial class MapStatisticsDialog : Window
{
    public MapStatisticsDialog()
    {
        InitializeComponent();
    }

    public void Populate(MapData map)
    {
        // Tiles
        TotalTilesText.Text = map.Tiles.Count.ToString("N0");
        UniquePositionsText.Text = map.Tiles.Count.ToString("N0");

        var floors = map.GetFloors();
        FloorsText.Text = $"{floors.Length}  ({string.Join(", ", floors)})";

        int houseTiles = 0;
        int totalItems = 0;
        var uniqueIds = new HashSet<ushort>();
        int actionIdItems = 0;
        int uniqueIdItems = 0;
        int teleportItems = 0;
        int depotItems = 0;

        foreach (var tile in map.Tiles.Values)
        {
            if (tile.HouseId > 0) houseTiles++;
            CountItems(tile.Items, uniqueIds, ref totalItems, ref actionIdItems,
                        ref uniqueIdItems, ref teleportItems, ref depotItems);
        }

        HouseTilesText.Text = houseTiles.ToString("N0");

        // Items
        TotalItemsText.Text = totalItems.ToString("N0");
        UniqueItemsText.Text = uniqueIds.Count.ToString("N0");
        ActionIdItemsText.Text = actionIdItems.ToString("N0");
        UniqueIdItemsText.Text = uniqueIdItems.ToString("N0");
        TeleportItemsText.Text = teleportItems.ToString("N0");
        DepotItemsText.Text = depotItems.ToString("N0");

        // Towns & Waypoints
        TownsText.Text = map.Towns.Count.ToString("N0");
        WaypointsText.Text = map.Waypoints.Count.ToString("N0");

        // Dimensions
        HeaderSizeText.Text = $"{map.Width} × {map.Height}";
        var (minX, minY, maxX, maxY) = map.GetBounds();
        if (map.Tiles.Count > 0)
        {
            BoundsXText.Text = $"{minX} – {maxX}  (span {maxX - minX + 1})";
            BoundsYText.Text = $"{minY} – {maxY}  (span {maxY - minY + 1})";
        }
        else
        {
            BoundsXText.Text = "—";
            BoundsYText.Text = "—";
        }
    }

    private static void CountItems(List<MapItem> items, HashSet<ushort> uniqueIds,
        ref int total, ref int actionId, ref int uniqueId, ref int teleport, ref int depot)
    {
        foreach (var item in items)
        {
            total++;
            uniqueIds.Add(item.Id);
            if (item.ActionId > 0) actionId++;
            if (item.UniqueId > 0) uniqueId++;
            if (item.TeleportDestination.HasValue) teleport++;
            if (item.DepotId > 0) depot++;

            if (item.Contents.Count > 0)
                CountItems(item.Contents, uniqueIds, ref total, ref actionId,
                            ref uniqueId, ref teleport, ref depot);
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
