using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using POriginsItemEditor.App.Controls;
using POriginsItemEditor.App.ViewModels;
using POriginsItemEditor.OTB;

namespace POriginsItemEditor.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += OnWindowClosing;
        Loaded += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                // Wire map canvas events
                var mapCanvas = this.FindControl<MapCanvasControl>("MapCanvas");
                if (mapCanvas != null)
                {
                    vm._mapCenterRequested = () => mapCanvas.CenterOnMap();
                    vm._mapGoToRequested = (x, y, z) => mapCanvas.GoToPosition(x, y, z);
                    mapCanvas.SaveRequested += () => vm.SaveMap();
                    mapCanvas.MapEdited += () => vm.MarkMapDirty();
                    mapCanvas.ActionLogged += msg => vm.AddMapLog(msg);
                    mapCanvas.SelectedTileChanged += pos => vm.OnSelectedTileChanged(pos);
                    mapCanvas.SelectRawRequested += serverId =>
                    {
                        vm.Palette?.NavigateToCatalogItem(serverId);
                    };
                    mapCanvas.LookupInCollectionRequested += serverId =>
                    {
                        if (vm.Palette?.NavigateToSubCollection(serverId) != true)
                            vm.AddMapLog($"Item {serverId} not found in any collection.");
                    };
                }

                await vm.TryLoadLastSessionAsync();

                // Wire brush database to map canvas after data is loaded
                if (mapCanvas != null && vm.BrushDb != null)
                    mapCanvas.BrushDb = vm.BrushDb;

                // Listen for future BrushDb loads
                if (mapCanvas != null)
                {
                    vm.PropertyChanged += (_, args) =>
                    {
                        if (args.PropertyName == nameof(vm.BrushDb))
                            mapCanvas.BrushDb = vm.BrushDb;
                    };
                }
            }
        };
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.SaveSessionsToSettings();
    }

    private void OnMinimapSwatchClicked(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control)
            FlyoutBase.ShowAttachedFlyout(control);
    }

    private void OnSessionTabPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: SessionViewModel session }
            && DataContext is MainWindowViewModel vm)
        {
            vm.SwitchToSession(session);

            // Re-wire map canvas for the new session
            var mapCanvas = this.FindControl<MapCanvasControl>("MapCanvas");
            if (mapCanvas != null && vm.BrushDb != null)
                mapCanvas.BrushDb = vm.BrushDb;
        }
    }

    private void OnMinimapColorPicked(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || DataContext is not MainWindowViewModel vm || vm.SelectedItem is not { } item)
            return;

        ushort index = border.Tag switch
        {
            ushort u => u,
            int i => (ushort)i,
            _ => ushort.MaxValue,
        };
        if (index > 215) return;

        item.MinimapColor = index;

        Control? current = border;
        while (current != null)
        {
            var flyout = FlyoutBase.GetAttachedFlyout(current);
            if (flyout != null)
            {
                flyout.Hide();
                break;
            }
            current = current.Parent as Control;
        }
    }

    private void OnCompositionSpriteDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border
            && border.DataContext is SpriteViewModel svm
            && DataContext is MainWindowViewModel vm)
        {
            vm.NavigateRightSpriteToIdCommand.Execute(svm.SpriteId);
        }
    }

    private void OnClientItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.OpenClientItemEditor();
    }

    private void OnOtbItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.OpenOtbItemEditor();
    }

    // ── Palette event handlers ──

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;

        if (e.Key == Key.Enter)
        {
            if (tb.DataContext is PaletteCollectionViewModel col)
                col.CommitRenameCommand.Execute(null);
            else if (tb.DataContext is PaletteSubCollectionViewModel sub)
                sub.CommitRenameCommand.Execute(null);

            // Also save to config
            if (DataContext is MainWindowViewModel vm && vm.Palette is { } palette)
                palette.SaveAfterRename();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (tb.DataContext is PaletteCollectionViewModel col)
                col.CancelRenameCommand.Execute(null);
            else if (tb.DataContext is PaletteSubCollectionViewModel sub)
                sub.CancelRenameCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnPaletteSubCollectionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is PaletteSubCollectionViewModel sub
            && DataContext is MainWindowViewModel vm && vm.Palette is { } palette)
        {
            palette.SelectedSubCollection = sub;
        }
    }

    private void OnPaletteSubCollectionDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is PaletteSubCollectionViewModel sub
            && DataContext is MainWindowViewModel vm && vm.Palette is { } palette)
        {
            palette.ShowSubCollectionItems(sub);
        }
    }

    private void OnPaletteItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is PaletteItemViewModel item
            && DataContext is MainWindowViewModel vm && vm.Palette is { } palette)
        {
            palette.SelectedPaletteItem = item;
            vm.BrushServerId = item.ServerId;
        }
    }

    private void OnRemoveItemFromSubCollection(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is PaletteItemViewModel item
            && DataContext is MainWindowViewModel vm && vm.Palette is { } palette)
        {
            palette.RemoveItemFromSelectedCommand.Execute(item);
        }
    }

    private void OnCatalogItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border && border.DataContext is PaletteItemViewModel item
            && DataContext is MainWindowViewModel vm && vm.Palette is { } palette)
        {
            palette.AddCatalogItemToSelectedCommand.Execute(item);
        }
    }

    private void OnCatalogItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is PaletteItemViewModel item
            && DataContext is MainWindowViewModel vm)
        {
            vm.BrushServerId = item.ServerId;
        }
    }

    private void OnAddCatalogItemToSubCollection(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is PaletteItemViewModel item
            && DataContext is MainWindowViewModel vm && vm.Palette is { } palette)
        {
            palette.AddCatalogItemToSelectedCommand.Execute(item);
        }
    }

    private void OnNewSubCollectionFromContext(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is PaletteCollectionViewModel col
            && DataContext is MainWindowViewModel vm && vm.Palette is { } palette)
        {
            palette.SelectedCollection = col;
            palette.AddSubCollectionCommand.Execute(null);
        }
    }

    private void OnDeleteCollectionFromContext(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is PaletteCollectionViewModel col
            && DataContext is MainWindowViewModel vm && vm.Palette is { } palette)
        {
            palette.RemoveCollectionCommand.Execute(col);
        }
    }

    private void OnDeleteSubCollectionFromContext(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is PaletteSubCollectionViewModel sub
            && DataContext is MainWindowViewModel vm && vm.Palette is { } palette)
        {
            palette.RemoveSubCollectionCommand.Execute(sub);
        }
    }
}