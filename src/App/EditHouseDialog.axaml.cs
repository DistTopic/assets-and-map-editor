using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AssetsAndMapEditor.OTB;

namespace AssetsAndMapEditor.App;

public partial class EditHouseDialog : Window
{
    private readonly MapHouse _house;
    private readonly List<MapTown> _towns;

    public EditHouseDialog(MapHouse house, List<MapTown> towns)
    {
        _house = house;
        _towns = towns;
        InitializeComponent();

        NameBox.Text = house.Name;
        IdSpin.Value = house.Id;
        RentSpin.Value = house.Rent;
        GuildhallCheck.IsChecked = house.Guildhall;

        // Populate town combo
        var townItems = towns.OrderBy(t => t.Id)
            .Select(t => new TownComboItem(t.Id, $"{t.Name} (#{t.Id})"))
            .ToList();
        TownCombo.ItemsSource = townItems;
        TownCombo.SelectedItem = townItems.FirstOrDefault(t => t.Id == house.TownId) ?? townItems.FirstOrDefault();
    }

    // Parameterless ctor for AXAML designer
    public EditHouseDialog() : this(new MapHouse { Id = 1, Name = "New House" }, []) { }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        _house.Name = NameBox.Text?.Trim() ?? "Unnamed";
        _house.Id = (uint)System.Math.Clamp(IdSpin.Value ?? 1, 1, 65535);
        _house.Rent = (int)System.Math.Clamp(RentSpin.Value ?? 0, 0, 999999999);
        _house.Guildhall = GuildhallCheck.IsChecked == true;

        if (TownCombo.SelectedItem is TownComboItem townItem)
            _house.TownId = townItem.Id;

        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
