using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
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
                    vm._mapSpriteCacheInvalidated = () => { mapCanvas.ClearCaches(); mapCanvas.InvalidateVisual(); };
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

                // Wire confirmation dialog for palette delete operations
                if (vm.Palette != null)
                    vm.Palette.ConfirmAsync = ShowConfirmDialogAsync;

                // Wire brush database to map canvas after data is loaded
                if (mapCanvas != null && vm.BrushDb != null)
                    mapCanvas.BrushDb = vm.BrushDb;
                if (mapCanvas != null && vm.BrushCatalog != null)
                    mapCanvas.BrushCatalog = vm.BrushCatalog;

                // Listen for future BrushDb loads
                if (mapCanvas != null)
                {
                    vm.PropertyChanged += (_, args) =>
                    {
                        if (args.PropertyName == nameof(vm.BrushDb))
                            mapCanvas.BrushDb = vm.BrushDb;
                        if (args.PropertyName == nameof(vm.BrushCatalog))
                            mapCanvas.BrushCatalog = vm.BrushCatalog;
                        if (args.PropertyName == nameof(vm.SplitMode))
                            ApplySplitLayout(vm.SplitMode);
                    };
                }
                else
                {
                    vm.PropertyChanged += (_, args) =>
                    {
                        if (args.PropertyName == nameof(vm.SplitMode))
                            ApplySplitLayout(vm.SplitMode);
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

    // ── Tab drag-reorder state ──
    private SessionViewModel? _dragSession;
    private Point _dragStart;
    private bool _isDraggingTab;

    private void OnSessionTabPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(sender as Visual).Properties.IsRightButtonPressed)
            return; // Let context menu handle right click

        if (sender is Control { DataContext: SessionViewModel session }
            && DataContext is MainWindowViewModel vm)
        {
            vm.SwitchToSession(session);

            // Re-wire map canvas for the new session
            var mapCanvas = this.FindControl<MapCanvasControl>("MapCanvas");
            if (mapCanvas != null && vm.BrushDb != null)
                mapCanvas.BrushDb = vm.BrushDb;
            if (mapCanvas != null && vm.BrushCatalog != null)
                mapCanvas.BrushCatalog = vm.BrushCatalog;

            // Start drag tracking
            _dragSession = session;
            _dragStart = e.GetPosition(this);
            _isDraggingTab = false;
            e.Pointer.Capture((IInputElement)sender);
        }
    }

    private void OnSessionTabPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragSession == null) return;

        var pos = e.GetPosition(this);
        var delta = pos - _dragStart;
        if (!_isDraggingTab && (Math.Abs(delta.X) > 6 || Math.Abs(delta.Y) > 6))
        {
            _isDraggingTab = true;
            if (sender is Control c) c.Cursor = new Cursor(StandardCursorType.DragMove);
        }
    }

    private void OnSessionTabPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragSession == null) return;

        var session = _dragSession;
        _dragSession = null;

        if (sender is Control c)
        {
            c.Cursor = new Cursor(StandardCursorType.Hand);
            e.Pointer.Capture(null);
        }

        if (!_isDraggingTab) return;
        _isDraggingTab = false;

        if (DataContext is not MainWindowViewModel vm) return;

        // Check if dropped on another tab → reorder
        var tabBar = this.FindControl<ItemsControl>("SessionTabBar");
        if (tabBar == null) return;

        var pos = e.GetPosition(tabBar);
        var panel = tabBar.GetVisualDescendants().OfType<StackPanel>().FirstOrDefault();
        if (panel == null) return;

        int fromIdx = vm.Sessions.IndexOf(session);
        int toIdx = -1;

        // Find which tab the pointer is over
        foreach (var child in panel.Children)
        {
            if (child is not Border tabBorder) continue;
            var bounds = tabBorder.TransformToVisual(tabBar);
            if (bounds == null) continue;
            var rect = new Rect(bounds.Value.Transform(new Point(0, 0)),
                               new Size(tabBorder.Bounds.Width, tabBorder.Bounds.Height));
            if (rect.Contains(pos))
            {
                if (child.DataContext is SessionViewModel targetSession)
                    toIdx = vm.Sessions.IndexOf(targetSession);
                break;
            }
        }

        if (toIdx >= 0 && toIdx != fromIdx)
            vm.Sessions.Move(fromIdx, toIdx);
    }

    // ── Split pane management ──

    private void OnSecondaryPanePressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ActivateSecondaryPane();

            // Re-wire map canvas
            var mapCanvas = this.FindControl<MapCanvasControl>("MapCanvas");
            if (mapCanvas != null && vm.BrushDb != null)
                mapCanvas.BrushDb = vm.BrushDb;
            if (mapCanvas != null && vm.BrushCatalog != null)
                mapCanvas.BrushCatalog = vm.BrushCatalog;
        }
    }

    private void ApplySplitLayout(SplitMode mode)
    {
        var splitContainer = this.FindControl<Grid>("SplitContainer");
        if (splitContainer == null) return;

        var splitterCol = splitContainer.ColumnDefinitions[1];
        var secondPaneCol = splitContainer.ColumnDefinitions[2];
        var splitterRow = splitContainer.RowDefinitions[1];
        var secondPaneRow = splitContainer.RowDefinitions[2];

        var vSplitter = this.FindControl<GridSplitter>("VSplitter");
        var hSplitter = this.FindControl<GridSplitter>("HSplitter");
        var secondaryPane = this.FindControl<Border>("SecondaryPane");

        if (vSplitter == null || hSplitter == null || secondaryPane == null) return;

        switch (mode)
        {
            case SplitMode.Right:
                // Horizontal split: two columns
                splitterCol.Width = new GridLength(6);
                secondPaneCol.Width = new GridLength(1, GridUnitType.Star);
                splitterRow.Height = new GridLength(0);
                secondPaneRow.Height = new GridLength(0);
                vSplitter.IsVisible = true;
                hSplitter.IsVisible = false;
                Grid.SetColumn(secondaryPane, 2);
                Grid.SetRow(secondaryPane, 0);
                Grid.SetColumn(vSplitter, 1);
                Grid.SetRow(vSplitter, 0);
                secondaryPane.IsVisible = true;
                break;

            case SplitMode.Down:
                // Vertical split: two rows
                splitterCol.Width = new GridLength(0);
                secondPaneCol.Width = new GridLength(0);
                splitterRow.Height = new GridLength(6);
                secondPaneRow.Height = new GridLength(1, GridUnitType.Star);
                vSplitter.IsVisible = false;
                hSplitter.IsVisible = true;
                Grid.SetColumn(secondaryPane, 0);
                Grid.SetRow(secondaryPane, 2);
                Grid.SetColumn(hSplitter, 0);
                Grid.SetRow(hSplitter, 1);
                secondaryPane.IsVisible = true;
                break;

            default: // None
                splitterCol.Width = new GridLength(0);
                secondPaneCol.Width = new GridLength(0);
                splitterRow.Height = new GridLength(0);
                secondPaneRow.Height = new GridLength(0);
                vSplitter.IsVisible = false;
                hSplitter.IsVisible = false;
                secondaryPane.IsVisible = false;
                break;
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

    // ── Sprite drag-drop: drag from sprite list → drop on composition cell ──

    private SpriteViewModel? _dragSprite;
    private Popup? _dragAdorner;
    private bool _spriteDragActive;

    private void OnRightSpritePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not SpriteViewModel svm)
            return;

        var props = e.GetCurrentPoint(border).Properties;
        if (!props.IsLeftButtonPressed) return;

        _dragSprite = svm;
        _spriteDragActive = false;
        e.Pointer.Capture(border);
        e.Handled = true;
    }

    private void OnRightSpritePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragSprite == null) return;

        if (!_spriteDragActive)
        {
            _spriteDragActive = true;
            // Create floating adorner showing the sprite image
            var img = new Image
            {
                Source = _dragSprite.Bitmap,
                Width = 32,
                Height = 32,
                Stretch = Stretch.None
            };
            RenderOptions.SetBitmapInterpolationMode(img, Avalonia.Media.Imaging.BitmapInterpolationMode.None);
            _dragAdorner = new Popup
            {
                IsLightDismissEnabled = false,
                Placement = PlacementMode.Pointer,
                PlacementTarget = this,
                Child = new Border
                {
                    Background = Brushes.Transparent,
                    Opacity = 0.85,
                    IsHitTestVisible = false,
                    Child = img
                }
            };
            ((Panel)this.Content!).Children.Add(_dragAdorner);
            _dragAdorner.Open();
        }

        // Reposition by re-opening at pointer
        if (_dragAdorner != null)
        {
            _dragAdorner.Close();
            _dragAdorner.Open();
        }
    }

    private void OnRightSpritePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragSprite == null) return;

        var draggedSprite = _dragSprite;
        _dragSprite = null;
        _spriteDragActive = false;

        // Close and remove adorner
        if (_dragAdorner != null)
        {
            _dragAdorner.Close();
            if (this.Content is Panel panel)
                panel.Children.Remove(_dragAdorner);
            _dragAdorner = null;
        }

        e.Pointer.Capture(null);

        // Hit-test: find the composition cell under the pointer
        var pos = e.GetPosition(this);
        var hit = this.InputHitTest(pos);
        var visual = hit as Control;
        SpriteViewModel? targetSlot = null;
        while (visual != null)
        {
            if (visual.DataContext is SpriteViewModel svm && svm.SlotIndex >= 0)
            {
                targetSlot = svm;
                break;
            }
            visual = visual.Parent as Control;
        }

        if (targetSlot != null && DataContext is MainWindowViewModel vm)
            vm.AssignSpriteToSlot(targetSlot, draggedSprite.SpriteId);
    }

    private void OnClientItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.NavigateToClientItem();
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
            if (vm.Palette != null) vm.Palette.SelectedBrush = null;

            // Ground/doodad brush → populate BrushItemIds so auto-bordering runs
            if (vm.BrushCatalog?.GroundsByName.TryGetValue(item.Name, out var ground) == true
                && ground.Items.Count > 0)
            {
                vm.BrushItemIds = ground.Items.Select(ci => ci.Id).ToList();
            }
            else
            {
                vm.BrushItemIds = null; // raw item — no auto-bordering
            }
        }
    }

    private void OnCatalogContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is not ContextMenu ctx) return;
        if (DataContext is not MainWindowViewModel vm || vm.Palette is not { } palette) return;

        // Find the "Add to Collection" placeholder (first MenuItem)
        var addToRoot = ctx.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header is string h && h == "Add to Collection");
        if (addToRoot == null) return;

        addToRoot.Items.Clear();

        // Resolve the catalog item from the parent Border's DataContext
        var catalogItem = ctx.DataContext as PaletteItemViewModel
                          ?? (ctx.PlacementTarget as Avalonia.Controls.Control)?.DataContext as PaletteItemViewModel;

        // Store the catalog item in the ContextMenu's Tag so click handlers can retrieve it
        ctx.Tag = catalogItem;

        foreach (var col in palette.Collections)
        {
            if (col.IsBuiltIn) continue; // skip Raw

            var colMi = new MenuItem { Header = col.Name };

            foreach (var sub in col.SubCollections)
            {
                if (sub.IsBuiltIn) continue;

                if (sub.SubSubCollections.Count > 0)
                {
                    // SubCollection has children → submenu
                    var subMi = new MenuItem { Header = sub.Name };

                    // Option to add directly to the sub-collection
                    var directMi = new MenuItem { Header = $"{sub.Name} (root)", Tag = sub };
                    directMi.Click += OnAddCatalogItemToSpecificSubCollection;
                    subMi.Items.Add(directMi);
                    subMi.Items.Add(new Separator());

                    foreach (var subsub in sub.SubSubCollections)
                    {
                        var subsubMi = new MenuItem { Header = subsub.Name, Tag = subsub };
                        subsubMi.Click += OnAddCatalogItemToSubSubCollection;
                        subMi.Items.Add(subsubMi);
                    }
                    colMi.Items.Add(subMi);
                }
                else
                {
                    // Leaf sub-collection → direct click
                    var subMi = new MenuItem { Header = sub.Name, Tag = sub };
                    subMi.Click += OnAddCatalogItemToSpecificSubCollection;
                    colMi.Items.Add(subMi);
                }
            }

            if (colMi.Items.Count > 0)
                addToRoot.Items.Add(colMi);
        }

        addToRoot.IsEnabled = addToRoot.Items.Count > 0;
    }

    /// <summary>Walk up the visual/logical tree from a MenuItem to find the owning ContextMenu.</summary>
    private static PaletteItemViewModel? GetCatalogItemFromMenuItem(MenuItem mi)
    {
        // Walk up parent MenuItems to find the ContextMenu
        Avalonia.Controls.Control? current = mi;
        while (current != null)
        {
            if (current is ContextMenu ctx)
                return ctx.Tag as PaletteItemViewModel;
            current = current.Parent as Avalonia.Controls.Control;
        }
        return null;
    }

    private void OnAddCatalogItemToSpecificSubCollection(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is PaletteSubCollectionViewModel sub
            && DataContext is MainWindowViewModel vm && vm.Palette is { } palette)
        {
            var item = GetCatalogItemFromMenuItem(mi);
            if (item != null)
                palette.AddItemToSubCollection(sub, item.ServerId);
        }
    }

    private void OnAddCatalogItemToSubSubCollection(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is PaletteSubSubCollectionViewModel subsub
            && DataContext is MainWindowViewModel vm && vm.Palette is { } palette)
        {
            var item = GetCatalogItemFromMenuItem(mi);
            if (item != null)
                palette.AddItemToSubSubCollection(subsub, item.ServerId);
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

    private void OnAddCatalogItemToBrush(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is PaletteItemViewModel item
            && DataContext is MainWindowViewModel vm && vm.Palette is { } palette)
        {
            palette.AddItemToBrushCommand.Execute(item);
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

    private void OnClientItemSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb && DataContext is MainWindowViewModel vm)
        {
            vm.SelectedClientItemsList = lb.SelectedItems?
                .OfType<ClientItemViewModel>()
                .ToList() ?? [];
        }
    }

    private void OnOtbItemSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb && DataContext is MainWindowViewModel vm)
        {
            vm.SelectedOtbItemsList = lb.SelectedItems?
                .OfType<ItemViewModel>()
                .ToList() ?? [];
        }
    }

    private void OnOtbItemContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var selCount = vm.SelectedOtbItemsList.Count;

        var removeMi = this.FindControl<MenuItem>("RemoveOtbSelectedMenuItem");
        if (removeMi != null)
        {
            removeMi.IsEnabled = selCount > 0;
            removeMi.Header = selCount > 1
                ? $"Remove {selCount} OTB Items"
                : "Remove Selected";

            removeMi.Click -= OnRemoveOtbSelectedFromContext;
            removeMi.Click += OnRemoveOtbSelectedFromContext;
        }
    }

    private void OnRemoveOtbSelectedFromContext(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.RemoveOtbItemsCommand.Execute(null);
    }

    private void OnClientItemContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var selCount = vm.SelectedClientItemsList.Count;

        // ── Transplant sub-menu ──
        var menuItem = this.FindControl<MenuItem>("TransplantToMenuItem");
        if (menuItem != null)
        {
            menuItem.Items.Clear();
            var targets = vm.Sessions
                .Where(s => s != vm.ActiveSession && s.DatData != null)
                .ToList();

            if (targets.Count == 0 || selCount == 0)
            {
                menuItem.IsEnabled = false;
                menuItem.Header = selCount == 0
                    ? "Select items first"
                    : "No other sessions available";
            }
            else
            {
                menuItem.IsEnabled = true;
                menuItem.Header = $"Transplant {selCount} item(s) to...";

                foreach (var target in targets)
                {
                    var mi = new MenuItem { Header = target.Name, Tag = target };
                    mi.Click += async (_, _) =>
                    {
                        await vm.TransplantMultipleItemsAsync(target);
                    };
                    menuItem.Items.Add(mi);
                }
            }
        }

        // ── Create OTB Item ──
        var createOtbMi = this.FindControl<MenuItem>("CreateOtbItemMenuItem");
        if (createOtbMi != null)
        {
            createOtbMi.IsEnabled = selCount > 0;
            createOtbMi.Header = selCount > 1
                ? $"Create {selCount} OTB Items"
                : "Create OTB Item";

            // Rewire click handler (detach old, attach new)
            createOtbMi.Click -= OnCreateOtbItemFromContext;
            createOtbMi.Click += OnCreateOtbItemFromContext;
        }

        // ── Remove Selected ──
        var removeMi = this.FindControl<MenuItem>("RemoveSelectedMenuItem");
        if (removeMi != null)
        {
            removeMi.IsEnabled = selCount > 0;
            removeMi.Header = selCount > 1
                ? $"Remove {selCount} Items"
                : "Remove Selected";

            removeMi.Click -= OnRemoveSelectedFromContext;
            removeMi.Click += OnRemoveSelectedFromContext;
        }

        // ── Copy Item ──
        var copyMi = this.FindControl<MenuItem>("CopyClientItemMenuItem");
        if (copyMi != null)
        {
            copyMi.IsEnabled = selCount == 1;
            copyMi.Click -= OnCopyClientItem;
            copyMi.Click += OnCopyClientItem;
        }

        // ── Replace with... ──
        var replaceMi = this.FindControl<MenuItem>("ReplaceClientItemMenuItem");
        if (replaceMi != null)
            replaceMi.IsEnabled = selCount == 1;

        var replaceClipMi = this.FindControl<MenuItem>("ReplaceFromClipboardMenuItem");
        if (replaceClipMi != null)
        {
            replaceClipMi.IsEnabled = selCount == 1 && vm.HasCopiedClientItem;
            replaceClipMi.Click -= OnReplaceFromClipboard;
            replaceClipMi.Click += OnReplaceFromClipboard;
        }

        var replaceObdMi = this.FindControl<MenuItem>("ReplaceFromObdMenuItem");
        if (replaceObdMi != null)
        {
            replaceObdMi.IsEnabled = selCount == 1;
            replaceObdMi.Click -= OnReplaceFromObd;
            replaceObdMi.Click += OnReplaceFromObd;
        }
    }

    private void OnCopyClientItem(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.CopyClientItem();
    }

    private void OnReplaceFromClipboard(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.ReplaceClientItemFromClipboard();
    }

    private async void OnReplaceFromObd(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            await vm.ReplaceClientItemFromObdAsync();
    }

    private void OnCreateOtbItemFromContext(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.CreateServerItemsFromSelection();
    }

    private void OnRemoveSelectedFromContext(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.RemoveThingCommand.Execute(null);
    }

    // ── Brush size / shape / zone handlers ──

    private void OnBrushSizeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out var size)
            && DataContext is MainWindowViewModel vm)
            vm.BrushSize = size;
    }

    private void OnBrushShapeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && DataContext is MainWindowViewModel vm)
            vm.BrushCircle = btn.Tag?.ToString() == "circle";
    }

    private void OnZoneBrushClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out var zone)
            && DataContext is MainWindowViewModel vm)
        {
            // Toggle: if already active, deactivate
            vm.ActiveZoneBrush = vm.ActiveZoneBrush == zone ? 0 : zone;
        }
    }

    // ── Inline rename handlers (Enter = commit, Escape = cancel) ──

    private void OnCollectionRenameKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.Palette?.SelectedCollection == null) return;
        var col = vm.Palette.SelectedCollection;
        if (e.Key == Avalonia.Input.Key.Enter)
        { col.CommitRenameCommand.Execute(null); vm.Palette.SaveAfterRename(); e.Handled = true; }
        else if (e.Key == Avalonia.Input.Key.Escape)
        { col.CancelRenameCommand.Execute(null); e.Handled = true; }
    }

    private void OnSubCollectionRenameKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.Palette?.SelectedSubCollection == null) return;
        var sub = vm.Palette.SelectedSubCollection;
        if (e.Key == Avalonia.Input.Key.Enter)
        { sub.CommitRenameCommand.Execute(null); vm.Palette.SaveAfterRename(); e.Handled = true; }
        else if (e.Key == Avalonia.Input.Key.Escape)
        { sub.CancelRenameCommand.Execute(null); e.Handled = true; }
    }

    private void OnSubSubCollectionRenameKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.Palette?.SelectedSubSubCollection == null) return;
        var subSub = vm.Palette.SelectedSubSubCollection;
        if (e.Key == Avalonia.Input.Key.Enter)
        { subSub.CommitRenameCommand.Execute(null); vm.Palette.SaveAfterRename(); e.Handled = true; }
        else if (e.Key == Avalonia.Input.Key.Escape)
        { subSub.CancelRenameCommand.Execute(null); e.Handled = true; }
    }

    private void OnBrushRenameKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.Palette?.SelectedBrush == null) return;
        var brush = vm.Palette.SelectedBrush;
        if (e.Key == Avalonia.Input.Key.Enter)
        { brush.CommitRenameCommand.Execute(null); vm.Palette.SaveAfterRename(); e.Handled = true; }
        else if (e.Key == Avalonia.Input.Key.Escape)
        { brush.CancelRenameCommand.Execute(null); e.Handled = true; }
    }

    private async Task<bool> ShowConfirmDialogAsync(string title, string message)
    {
        var tcs = new TaskCompletionSource<bool>();

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        DockPanel.SetDock(btnPanel, Dock.Bottom);

        var msgBlock = new TextBlock
        {
            Text = message,
            Foreground = Brush.Parse("#cdd6f4"),
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var dock = new DockPanel { Margin = new Thickness(20, 16) };
        dock.Children.Add(btnPanel);
        dock.Children.Add(msgBlock);

        var dialog = new Window
        {
            Title = title,
            Width = 340, Height = 150,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush.Parse("#1e1e2e"),
            Content = dock
        };

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Background = Brush.Parse("#313244"),
            Foreground = Brush.Parse("#cdd6f4"),
            Padding = new Thickness(16, 6),
            CornerRadius = new CornerRadius(6),
        };
        var confirmBtn = new Button
        {
            Content = "Delete",
            Background = Brush.Parse("#f38ba8"),
            Foreground = Brush.Parse("#1e1e2e"),
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(16, 6),
            CornerRadius = new CornerRadius(6),
        };
        cancelBtn.Click += (_, _) => { tcs.TrySetResult(false); dialog.Close(); };
        confirmBtn.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
        dialog.Closing += (_, _) => tcs.TrySetResult(false);

        btnPanel.Children.Add(cancelBtn);
        btnPanel.Children.Add(confirmBtn);

        await dialog.ShowDialog(this);
        return await tcs.Task;
    }
}