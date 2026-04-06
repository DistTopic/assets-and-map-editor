using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using AssetsAndMapEditor.OTB;
using AssetsAndMapEditor.App.ViewModels;

namespace AssetsAndMapEditor.App;

// ── Sync direction ──────────────────────────────────────────────────────

public enum SyncDirection
{
    Skip,    // no change
    UseOtb,  // apply OTB value → DAT
    UseDat,  // apply DAT value → OTB
}

/// <summary>
/// A single divergent property between DAT and OTB.
/// The user picks which side wins (or skips).
/// </summary>
public sealed class PropertyDifference : INotifyPropertyChanged
{
    private SyncDirection _direction = SyncDirection.Skip;

    public SyncDirection Direction
    {
        get => _direction;
        set { _direction = value; NotifyAll(); }
    }

    public required string PropertyName { get; init; }
    public required string OtbValue { get; init; }
    public required string DatValue { get; init; }

    /// <summary>Applies the DAT value onto the OTB item.</summary>
    public required Action<OtbItem> ApplyToOtb { get; init; }
    /// <summary>Applies the OTB value onto the DatThingType.</summary>
    public required Action<DatThingType> ApplyToDat { get; init; }

    /// <summary>When true, OTB/DAT values are Tibia 8-bit color indices — show a swatch.</summary>
    public bool IsColor { get; init; }
    public Color? OtbColor { get; init; }
    public Color? DatColor { get; init; }
    public IBrush? OtbColorSwatch => OtbColor.HasValue ? new SolidColorBrush(OtbColor.Value) : null;
    public IBrush? DatColorSwatch => DatColor.HasValue ? new SolidColorBrush(DatColor.Value) : null;

    // ── Visual helpers bound from XAML ──

    private static readonly IBrush s_activeBg = new SolidColorBrush(Color.Parse("#89b4fa"));
    private static readonly IBrush s_activeFg = new SolidColorBrush(Color.Parse("#1e1e2e"));
    private static readonly IBrush s_inactiveBg = new SolidColorBrush(Color.Parse("#313244"));
    private static readonly IBrush s_otbInactiveFg = new SolidColorBrush(Color.Parse("#f38ba8"));
    private static readonly IBrush s_datInactiveFg = new SolidColorBrush(Color.Parse("#89b4fa"));
    private static readonly IBrush s_skipArrow = new SolidColorBrush(Color.Parse("#585b70"));
    private static readonly IBrush s_otbArrow = new SolidColorBrush(Color.Parse("#f38ba8"));
    private static readonly IBrush s_datArrow = new SolidColorBrush(Color.Parse("#a6e3a1"));

    public IBrush OtbBackground => Direction == SyncDirection.UseOtb ? s_activeBg : s_inactiveBg;
    public IBrush OtbForeground => Direction == SyncDirection.UseOtb ? s_activeFg : s_otbInactiveFg;
    public IBrush DatBackground => Direction == SyncDirection.UseDat ? s_activeBg : s_inactiveBg;
    public IBrush DatForeground => Direction == SyncDirection.UseDat ? s_activeFg : s_datInactiveFg;

    public string DirectionArrow => Direction switch
    {
        SyncDirection.UseOtb => "←",
        SyncDirection.UseDat => "→",
        _ => "·"
    };
    public IBrush ArrowColor => Direction switch
    {
        SyncDirection.UseOtb => s_otbArrow,
        SyncDirection.UseDat => s_datArrow,
        _ => s_skipArrow
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void NotifyAll()
    {
        Notify(nameof(Direction));
        Notify(nameof(OtbBackground)); Notify(nameof(OtbForeground));
        Notify(nameof(DatBackground)); Notify(nameof(DatForeground));
        Notify(nameof(DirectionArrow)); Notify(nameof(ArrowColor));
    }
    private void Notify([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new(n));
}

/// <summary>Groups all divergent properties for one item.</summary>
public sealed class DivergentItem
{
    public required OtbItem OtbItem { get; init; }
    public required DatThingType DatThing { get; init; }
    public required List<PropertyDifference> Differences { get; init; }
}

/// <summary>View-model wrapper for the left sidebar list.</summary>
public sealed class DivergentItemVm : INotifyPropertyChanged
{
    public required DivergentItem Model { get; init; }

    private WriteableBitmap? _sprite;
    public WriteableBitmap? Sprite
    {
        get => _sprite;
        set { _sprite = value; PropertyChanged?.Invoke(this, new(nameof(Sprite))); }
    }

    public int AnimFrame { get; set; }
    public int TotalFrames => Model.DatThing.FrameGroups.Length > 0
        ? Model.DatThing.FrameGroups[0].Frames : 1;

    public string ServerIdText => Model.OtbItem.ServerId.ToString();
    public string ClientIdText => Model.OtbItem.ClientId.ToString();
    public string Name => string.IsNullOrEmpty(Model.OtbItem.Name) ? "" : Model.OtbItem.Name;
    public string DiffCountText => $"{Model.Differences.Count} diff(s)";

    public string DirectionIndicator
    {
        get
        {
            int dat = Model.Differences.Count(d => d.Direction == SyncDirection.UseDat);
            int otb = Model.Differences.Count(d => d.Direction == SyncDirection.UseOtb);
            if (dat > 0 && otb > 0) return "↔";
            if (dat > 0) return "→D";
            if (otb > 0) return "←O";
            return "–";
        }
    }

    public IBrush IndicatorColor
    {
        get
        {
            int dat = Model.Differences.Count(d => d.Direction == SyncDirection.UseDat);
            int otb = Model.Differences.Count(d => d.Direction == SyncDirection.UseOtb);
            if (dat > 0 && otb > 0) return new SolidColorBrush(Color.Parse("#f9e2af"));
            if (dat > 0) return new SolidColorBrush(Color.Parse("#a6e3a1"));
            if (otb > 0) return new SolidColorBrush(Color.Parse("#f38ba8"));
            return new SolidColorBrush(Color.Parse("#585b70"));
        }
    }

    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new(nameof(DirectionIndicator)));
        PropertyChanged?.Invoke(this, new(nameof(IndicatorColor)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Result returned after user confirms.</summary>
public sealed class SyncResult
{
    public int OtbPropertiesChanged { get; init; }
    public int DatPropertiesChanged { get; init; }
    public int ItemsAffected { get; init; }
    public bool DatModified { get; init; }
}

public sealed partial class DatOtbSyncWindow : Window
{
    private readonly List<DivergentItemVm> _itemVms;
    private readonly SprFile? _sprFile;
    private readonly DispatcherTimer _animTimer;
    private int _animTick;

    public SyncResult? Result { get; private set; }

    public DatOtbSyncWindow() : this([], null, null) { }

    public DatOtbSyncWindow(List<DivergentItem> divergentItems, DatData? datData, SprFile? sprFile)
    {
        InitializeComponent();
        _sprFile = sprFile;

        _itemVms = divergentItems.Select(d =>
        {
            var vm = new DivergentItemVm { Model = d };
            if (sprFile != null)
                vm.Sprite = MainWindowViewModel.ComposeThingBitmapStatic(d.DatThing, sprFile);
            return vm;
        }).ToList();

        ItemCountLabel.Text = $"{_itemVms.Count} divergent item(s) found";
        ItemListBox.ItemsSource = _itemVms;

        if (_itemVms.Count > 0)
        {
            ItemListBox.SelectedIndex = 0;
            ApplyButton.IsEnabled = true;
        }

        UpdateSummary();

        // Start animation timer
        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _animTimer.Tick += OnAnimTick;
        _animTimer.Start();

        Closed += (_, _) => { _animTimer.Stop(); _animTimer.Tick -= OnAnimTick; };
    }

    // ── Animation ───────────────────────────────────────────────────────

    private void OnAnimTick(object? sender, EventArgs e)
    {
        if (_sprFile == null) return;
        _animTick++;
        if (_animTick % 5 != 0) return; // every 500ms for items

        foreach (var vm in _itemVms)
        {
            if (vm.TotalFrames <= 1) continue;
            vm.AnimFrame = (vm.AnimFrame + 1) % vm.TotalFrames;
            vm.Sprite = MainWindowViewModel.ComposeThingBitmapStatic(vm.Model.DatThing, _sprFile, vm.AnimFrame);
        }

        // Update detail sprite if current selection is animated
        if (ItemListBox.SelectedItem is DivergentItemVm sel)
            DetailSprite.Source = sel.Sprite;
    }

    // ── List selection ──────────────────────────────────────────────────

    private void ItemListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ItemListBox.SelectedItem is DivergentItemVm vm)
            ShowItemDetail(vm);
    }

    private void ShowItemDetail(DivergentItemVm vm)
    {
        var item = vm.Model;
        ServerIdLabel.Text = item.OtbItem.ServerId.ToString();
        ClientIdLabel.Text = item.OtbItem.ClientId.ToString();
        ItemNameLabel.Text = string.IsNullOrEmpty(item.OtbItem.Name) ? "" : item.OtbItem.Name;
        GroupLabel.Text = item.OtbItem.Group.ToString();
        DiffCountLabel.Text = $"{item.Differences.Count} divergent property(ies)";
        DetailSprite.Source = vm.Sprite;
        PropertiesList.ItemsSource = item.Differences;
    }

    // ── Value click handlers ────────────────────────────────────────────

    private void OtbValueBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PropertyDifference diff })
        {
            diff.Direction = diff.Direction == SyncDirection.UseOtb ? SyncDirection.Skip : SyncDirection.UseOtb;
            RefreshCurrent();
        }
    }

    private void DatValueBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PropertyDifference diff })
        {
            diff.Direction = diff.Direction == SyncDirection.UseDat ? SyncDirection.Skip : SyncDirection.UseDat;
            RefreshCurrent();
        }
    }

    // ── Batch actions (current item) ────────────────────────────────────

    private void AllUseOtb_Click(object? sender, RoutedEventArgs e)
    {
        if (ItemListBox.SelectedItem is not DivergentItemVm vm) return;
        foreach (var d in vm.Model.Differences) d.Direction = SyncDirection.UseOtb;
        RefreshCurrent();
    }

    private void AllUseDat_Click(object? sender, RoutedEventArgs e)
    {
        if (ItemListBox.SelectedItem is not DivergentItemVm vm) return;
        foreach (var d in vm.Model.Differences) d.Direction = SyncDirection.UseDat;
        RefreshCurrent();
    }

    private void AllSkip_Click(object? sender, RoutedEventArgs e)
    {
        if (ItemListBox.SelectedItem is not DivergentItemVm vm) return;
        foreach (var d in vm.Model.Differences) d.Direction = SyncDirection.Skip;
        RefreshCurrent();
    }

    private void RefreshCurrent()
    {
        if (ItemListBox.SelectedItem is DivergentItemVm vm) vm.Refresh();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        int toOtb = _itemVms.Sum(v => v.Model.Differences.Count(p => p.Direction == SyncDirection.UseDat));
        int toDat = _itemVms.Sum(v => v.Model.Differences.Count(p => p.Direction == SyncDirection.UseOtb));
        int total = _itemVms.Sum(v => v.Model.Differences.Count);
        int skipped = total - toOtb - toDat;
        SummaryLabel.Text = $"{toOtb} → OTB, {toDat} → DAT, {skipped} skipped  ({total} total)";
    }

    // ── Apply / Cancel ──────────────────────────────────────────────────

    private void ApplyButton_Click(object? sender, RoutedEventArgs e)
    {
        int otbChanged = 0, datChanged = 0, itemsAffected = 0;
        bool datModified = false;

        foreach (var vm in _itemVms)
        {
            bool touched = false;
            foreach (var diff in vm.Model.Differences)
            {
                if (diff.Direction == SyncDirection.UseDat)
                {
                    diff.ApplyToOtb(vm.Model.OtbItem);
                    otbChanged++;
                    touched = true;
                }
                else if (diff.Direction == SyncDirection.UseOtb)
                {
                    diff.ApplyToDat(vm.Model.DatThing);
                    datChanged++;
                    datModified = true;
                    touched = true;
                }
            }
            if (touched) itemsAffected++;
        }

        Result = new SyncResult
        {
            OtbPropertiesChanged = otbChanged,
            DatPropertiesChanged = datChanged,
            ItemsAffected = itemsAffected,
            DatModified = datModified,
        };
        Close();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }
}
