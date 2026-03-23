using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using POriginsItemEditor.OTB;

namespace POriginsItemEditor.App.Controls;

/// <summary>
/// Custom Avalonia control that renders an OTBM map using software bitmap rendering.
/// Supports zoom, pan, floor selection, keyboard navigation, sprite animation, and light effects.
/// </summary>
public sealed class MapCanvasControl : Control
{
    private const int TileSize = 32;
    private const int MiniMapTileSize = 2;
    private const int MaxLightIntensity = 8;

    // Viewport
    private double _viewX, _viewY;

    // Internal data refs (synced from styled properties)
    private MapData? _mapData;
    private DatData? _datData;
    private SprFile? _sprFile;
    private OtbData? _otbData;

    /// <summary>Autobordering brush database. Set from code-behind after loading XML files.</summary>
    public BrushDatabase? BrushDb { get; set; }

    // Server ID → Client ID lookup
    private Dictionary<ushort, ushort>? _serverToClientMap;
    private Dictionary<ushort, OtbItem>? _serverToOtbItemMap;

    // Caches: individual 32x32 sprite bitmaps keyed by sprite ID
    private readonly Dictionary<uint, byte[]?> _spriteRgbaCache = new(4096);
    private readonly Dictionary<uint, WriteableBitmap?> _spriteBitmapCache = new(4096);

    // Drag state
    private bool _isDragging;
    private Point _dragStart;
    private double _dragViewStartX, _dragViewStartY;
    private bool _rightClickWasDrag; // true if right-click moved enough to be a pan drag

    // Ghost brush cursor (screen position for rendering the brush preview)
    private Point _cursorScreenPos;
    private bool _cursorInsideCanvas;

    // ── Selection state ──
    private readonly HashSet<MapPosition> _selectedTiles = new();
    private bool _isSelecting;           // shift+drag rectangular selection in progress
    private bool _isAreaSelection;       // true when selection was made via shift+drag or Ctrl+A
    private MapPosition _selectionStart; // tile at start of drag
    private MapPosition _selectionEnd;   // tile at current mouse position

    // ── Copy/Paste buffer ──
    private Dictionary<MapPosition, MapTile>? _copyBuffer;
    private MapPosition _copyOrigin; // upper-left corner of copy buffer

    // ── Paste mode ──
    private bool _isPasting; // when true, ghost preview from buffer follows cursor

    // ── Move state ──
    private bool _isMoving;          // drag-moving selected tiles
    private MapPosition _moveStart;  // tile under mouse when move started

    // ── Undo/Redo ──
    private readonly Stack<Dictionary<MapPosition, MapTile?>> _undoStack = new();
    private readonly Stack<Dictionary<MapPosition, MapTile?>> _redoStack = new();
    private const int MaxUndoSteps = 100;

    // ── Drag-paint state ──
    private bool _isPainting;        // left mouse held with brush → continuous painting
    private bool _isZonePainting;    // true if painting zones (flags) instead of items
    private MapPosition _lastPaintedTile; // avoid duplicate paint on same tile
    private Dictionary<MapPosition, MapTile?>? _paintUndoSnapshot; // accumulated snapshot for batch undo

    // ── Events to ViewModel ──
    public event Action? SaveRequested;
    public event Action? MapEdited;            // fired on every edit (for dirty flag)
    public event Action<string>? ActionLogged; // strategic log messages
    public event Action<MapPosition?>? SelectedTileChanged; // single tile selected

    // Minimap
    private WriteableBitmap? _minimapBitmap;
    private bool _minimapDirty = true;

    // Animation
    private DispatcherTimer? _animationTimer;
    private readonly System.Diagnostics.Stopwatch _animationClock = System.Diagnostics.Stopwatch.StartNew();
    private bool _hasAnimatedItems;


    // ── Styled Properties (bindable from XAML) ──

    public static readonly StyledProperty<MapData?> MapDataSourceProperty =
        AvaloniaProperty.Register<MapCanvasControl, MapData?>(nameof(MapDataSource));

    public static readonly StyledProperty<DatData?> DatDataSourceProperty =
        AvaloniaProperty.Register<MapCanvasControl, DatData?>(nameof(DatDataSource));

    public static readonly StyledProperty<SprFile?> SprFileSourceProperty =
        AvaloniaProperty.Register<MapCanvasControl, SprFile?>(nameof(SprFileSource));

    public static readonly StyledProperty<OtbData?> OtbDataSourceProperty =
        AvaloniaProperty.Register<MapCanvasControl, OtbData?>(nameof(OtbDataSource));

    public static readonly StyledProperty<byte> CurrentFloorProperty =
        AvaloniaProperty.Register<MapCanvasControl, byte>(nameof(CurrentFloor), 7);

    public static readonly StyledProperty<double> ZoomLevelProperty =
        AvaloniaProperty.Register<MapCanvasControl, double>(nameof(ZoomLevel), 1.0);

    // ── View toggle properties (all bindable from XAML) ──

    public static readonly StyledProperty<bool> ShowAllFloorsProperty =
        AvaloniaProperty.Register<MapCanvasControl, bool>(nameof(ShowAllFloors), true);

    public static readonly StyledProperty<bool> ShowAnimationProperty =
        AvaloniaProperty.Register<MapCanvasControl, bool>(nameof(ShowAnimation), true);

    public static readonly StyledProperty<bool> ShowLightsProperty =
        AvaloniaProperty.Register<MapCanvasControl, bool>(nameof(ShowLights), true);

    public static readonly StyledProperty<bool> ShowGridProperty =
        AvaloniaProperty.Register<MapCanvasControl, bool>(nameof(ShowGrid), false);

    public static readonly StyledProperty<bool> ShowShadeProperty =
        AvaloniaProperty.Register<MapCanvasControl, bool>(nameof(ShowShade), true);

    public static readonly StyledProperty<bool> ShowAsMinimapProperty =
        AvaloniaProperty.Register<MapCanvasControl, bool>(nameof(ShowAsMinimap), false);

    public static readonly StyledProperty<bool> GhostItemsProperty =
        AvaloniaProperty.Register<MapCanvasControl, bool>(nameof(GhostItems), false);

    public static readonly StyledProperty<bool> GhostHigherFloorsProperty =
        AvaloniaProperty.Register<MapCanvasControl, bool>(nameof(GhostHigherFloors), false);

    public static readonly StyledProperty<bool> ShowSpecialProperty =
        AvaloniaProperty.Register<MapCanvasControl, bool>(nameof(ShowSpecial), true);

    public static readonly StyledProperty<bool> ShowHousesProperty =
        AvaloniaProperty.Register<MapCanvasControl, bool>(nameof(ShowHouses), true);

    public static readonly StyledProperty<bool> ShowWaypointsProperty =
        AvaloniaProperty.Register<MapCanvasControl, bool>(nameof(ShowWaypoints), true);

    public static readonly StyledProperty<bool> ShowTownsProperty =
        AvaloniaProperty.Register<MapCanvasControl, bool>(nameof(ShowTowns), false);

    public static readonly StyledProperty<bool> ShowPathingProperty =
        AvaloniaProperty.Register<MapCanvasControl, bool>(nameof(ShowPathing), false);

    public static readonly StyledProperty<bool> HighlightItemsProperty =
        AvaloniaProperty.Register<MapCanvasControl, bool>(nameof(HighlightItems), false);

    public static readonly StyledProperty<bool> ShowTooltipsProperty =
        AvaloniaProperty.Register<MapCanvasControl, bool>(nameof(ShowTooltips), true);

    public static readonly StyledProperty<bool> ShowIngameBoxProperty =
        AvaloniaProperty.Register<MapCanvasControl, bool>(nameof(ShowIngameBox), false);

    // ── Brush property (selected palette item for placement) ──

    public static readonly StyledProperty<ushort> BrushServerIdProperty =
        AvaloniaProperty.Register<MapCanvasControl, ushort>(nameof(BrushServerId), 0);

    public static readonly StyledProperty<int> BrushSizeProperty =
        AvaloniaProperty.Register<MapCanvasControl, int>(nameof(BrushSize), 0);

    public static readonly StyledProperty<bool> BrushCircleProperty =
        AvaloniaProperty.Register<MapCanvasControl, bool>(nameof(BrushCircle), false);

    public static readonly StyledProperty<int> ActiveZoneBrushProperty =
        AvaloniaProperty.Register<MapCanvasControl, int>(nameof(ActiveZoneBrush), 0);

    public static readonly StyledProperty<IList<ushort>?> BrushItemIdsProperty =
        AvaloniaProperty.Register<MapCanvasControl, IList<ushort>?>(nameof(BrushItemIds));

    private static readonly Random _brushRandom = new();

    // ── Direct Properties (output) ──

    public static readonly DirectProperty<MapCanvasControl, string> HoveredTileInfoProperty =
        AvaloniaProperty.RegisterDirect<MapCanvasControl, string>(
            nameof(HoveredTileInfo), o => o.HoveredTileInfo, (o, v) => o.HoveredTileInfo = v);

    public static readonly DirectProperty<MapCanvasControl, WriteableBitmap?> MinimapBitmapProperty =
        AvaloniaProperty.RegisterDirect<MapCanvasControl, WriteableBitmap?>(
            nameof(MinimapBitmap), o => o.MinimapBitmap);

    // ── Property accessors ──

    public MapData? MapDataSource
    {
        get => GetValue(MapDataSourceProperty);
        set => SetValue(MapDataSourceProperty, value);
    }

    public DatData? DatDataSource
    {
        get => GetValue(DatDataSourceProperty);
        set => SetValue(DatDataSourceProperty, value);
    }

    public SprFile? SprFileSource
    {
        get => GetValue(SprFileSourceProperty);
        set => SetValue(SprFileSourceProperty, value);
    }

    public OtbData? OtbDataSource
    {
        get => GetValue(OtbDataSourceProperty);
        set => SetValue(OtbDataSourceProperty, value);
    }

    public byte CurrentFloor
    {
        get => GetValue(CurrentFloorProperty);
        set => SetValue(CurrentFloorProperty, value);
    }

    public double ZoomLevel
    {
        get => GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, Math.Clamp(value, 0.125, 4.0));
    }

    // ── View toggle accessors ──

    public bool ShowAllFloors { get => GetValue(ShowAllFloorsProperty); set => SetValue(ShowAllFloorsProperty, value); }
    public bool ShowAnimation { get => GetValue(ShowAnimationProperty); set => SetValue(ShowAnimationProperty, value); }
    public bool ShowLights { get => GetValue(ShowLightsProperty); set => SetValue(ShowLightsProperty, value); }
    public bool ShowGrid { get => GetValue(ShowGridProperty); set => SetValue(ShowGridProperty, value); }
    public bool ShowShade { get => GetValue(ShowShadeProperty); set => SetValue(ShowShadeProperty, value); }
    public bool ShowAsMinimap { get => GetValue(ShowAsMinimapProperty); set => SetValue(ShowAsMinimapProperty, value); }
    public bool GhostItems { get => GetValue(GhostItemsProperty); set => SetValue(GhostItemsProperty, value); }
    public bool GhostHigherFloors { get => GetValue(GhostHigherFloorsProperty); set => SetValue(GhostHigherFloorsProperty, value); }
    public bool ShowSpecial { get => GetValue(ShowSpecialProperty); set => SetValue(ShowSpecialProperty, value); }
    public bool ShowHouses { get => GetValue(ShowHousesProperty); set => SetValue(ShowHousesProperty, value); }
    public bool ShowWaypoints { get => GetValue(ShowWaypointsProperty); set => SetValue(ShowWaypointsProperty, value); }
    public bool ShowTowns { get => GetValue(ShowTownsProperty); set => SetValue(ShowTownsProperty, value); }
    public bool ShowPathing { get => GetValue(ShowPathingProperty); set => SetValue(ShowPathingProperty, value); }
    public bool HighlightItems { get => GetValue(HighlightItemsProperty); set => SetValue(HighlightItemsProperty, value); }
    public bool ShowTooltips { get => GetValue(ShowTooltipsProperty); set => SetValue(ShowTooltipsProperty, value); }
    public bool ShowIngameBox { get => GetValue(ShowIngameBoxProperty); set => SetValue(ShowIngameBoxProperty, value); }
    public ushort BrushServerId { get => GetValue(BrushServerIdProperty); set => SetValue(BrushServerIdProperty, value); }
    public int BrushSize { get => GetValue(BrushSizeProperty); set => SetValue(BrushSizeProperty, value); }
    public bool BrushCircle { get => GetValue(BrushCircleProperty); set => SetValue(BrushCircleProperty, value); }
    public int ActiveZoneBrush { get => GetValue(ActiveZoneBrushProperty); set => SetValue(ActiveZoneBrushProperty, value); }
    public IList<ushort>? BrushItemIds { get => GetValue(BrushItemIdsProperty); set => SetValue(BrushItemIdsProperty, value); }

    private string _hoveredTileInfo = string.Empty;
    public string HoveredTileInfo
    {
        get => _hoveredTileInfo;
        private set => SetAndRaise(HoveredTileInfoProperty, ref _hoveredTileInfo, value);
    }

    public WriteableBitmap? MinimapBitmap
    {
        get
        {
            if (_minimapDirty && _mapData != null)
            {
                RenderMinimap();
                _minimapDirty = false;
            }
            return _minimapBitmap;
        }
    }

    // ── Constructor & property change ──

    public MapCanvasControl()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MapDataSourceProperty)
        {
            _mapData = change.GetNewValue<MapData?>();
            ClearCaches();
            _minimapDirty = true;
            if (_mapData != null) CenterOnMap();
            InvalidateVisual();
        }
        else if (change.Property == DatDataSourceProperty)
        {
            _datData = change.GetNewValue<DatData?>();
            ClearCaches();
            InvalidateVisual();
        }
        else if (change.Property == SprFileSourceProperty)
        {
            _sprFile = change.GetNewValue<SprFile?>();
            ClearCaches();
            InvalidateVisual();
        }
        else if (change.Property == OtbDataSourceProperty)
        {
            _otbData = change.GetNewValue<OtbData?>();
            RebuildServerToClientMap();
            ClearCaches();
            InvalidateVisual();
        }
        else if (change.Property == CurrentFloorProperty)
        {
            _minimapDirty = true;
            InvalidateVisual();
        }
        else if (change.Property == ZoomLevelProperty)
        {
            InvalidateVisual();
        }
        else if (change.Property == ShowAllFloorsProperty
              || change.Property == ShowAnimationProperty
              || change.Property == ShowLightsProperty
              || change.Property == ShowGridProperty
              || change.Property == ShowShadeProperty
              || change.Property == ShowAsMinimapProperty
              || change.Property == GhostItemsProperty
              || change.Property == GhostHigherFloorsProperty
              || change.Property == ShowSpecialProperty
              || change.Property == ShowHousesProperty
              || change.Property == ShowWaypointsProperty
              || change.Property == ShowTownsProperty
              || change.Property == ShowPathingProperty
              || change.Property == HighlightItemsProperty
              || change.Property == ShowTooltipsProperty
              || change.Property == ShowIngameBoxProperty
              || change.Property == BrushServerIdProperty
              || change.Property == BrushSizeProperty
              || change.Property == BrushCircleProperty
              || change.Property == ActiveZoneBrushProperty)
        {
            InvalidateVisual();
        }
    }

    private void RebuildServerToClientMap()
    {
        if (_otbData == null) { _serverToClientMap = null; _serverToOtbItemMap = null; return; }
        _serverToClientMap = new Dictionary<ushort, ushort>(_otbData.Items.Count);
        _serverToOtbItemMap = new Dictionary<ushort, OtbItem>(_otbData.Items.Count);
        foreach (var item in _otbData.Items)
        {
            if (item.ServerId != 0 && item.ClientId != 0)
                _serverToClientMap[item.ServerId] = item.ClientId;
            if (item.ServerId != 0)
                _serverToOtbItemMap[item.ServerId] = item;
        }
    }

    // ── Rendering ──

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        int bw = (int)bounds.Width;
        int bh = (int)bounds.Height;
        if (bw <= 0 || bh <= 0) return;

        double zoom = ZoomLevel;
        byte floor = CurrentFloor;
        long elapsedMs = ShowAnimation ? _animationClock.ElapsedMilliseconds : 0;
        bool showAsMinimap = ShowAsMinimap;
        bool ghostItems = GhostItems;
        bool ghostHigherFloors = GhostHigherFloors;
        bool showShade = ShowShade;
        bool showAllFloors = ShowAllFloors;
        bool showSpecial = ShowSpecial;
        bool showHouses = ShowHouses;
        bool showWaypoints = ShowWaypoints;
        bool showTowns = ShowTowns;
        bool showPathing = ShowPathing;
        bool highlightItems = HighlightItems;
        bool showGrid = ShowGrid;
        bool showLights = ShowLights;
        bool showIngameBox = ShowIngameBox;

        // Dark background
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x1b)), null,
            new Rect(0, 0, bw, bh));

        if (_mapData == null || _sprFile == null || _datData == null) return;

        // Visible tile range (expand by a few tiles to catch multi-tile items extending into view)
        double tilePixelSize = TileSize * zoom;
        int startTileX = (int)Math.Floor(_viewX / TileSize) - 2;
        int startTileY = (int)Math.Floor(_viewY / TileSize) - 2;
        int endTileX = (int)Math.Ceiling((_viewX + bw / zoom) / TileSize) + 1;
        int endTileY = (int)Math.Ceiling((_viewY + bh / zoom) / TileSize) + 1;

        startTileX = Math.Max(startTileX, 0);
        startTileY = Math.Max(startTileY, 0);
        endTileX = Math.Min(endTileX, 65535);
        endTileY = Math.Min(endTileY, 65535);

        // Collect lights while drawing tiles
        List<(int x, int y, byte intensity, byte color)>? lights =
            showLights ? new(128) : null;
        _hasAnimatedItems = false;

        // ── Determine floor range to render (matching reference map_drawer.cpp SetupVars) ──
        // GROUND_LAYER = 7. Floors 0-7 are surface, 8-15 are underground.
        // start_z: highest Z to begin drawing (drawn first = furthest back)
        // end_z: current floor (shade boundary)
        // superend_z: lowest Z to stop drawing (drawn last = closest to viewer)
        int startZ, endZ, superEndZ;
        if (showAllFloors)
        {
            if (floor <= 7)
            {
                startZ = 7;       // surface: start from ground level
                superEndZ = floor; // only show current floor and below (never above)
            }
            else
            {
                startZ = Math.Min(15, floor + 2); // underground: 2 floors below current
                superEndZ = floor; // only show current floor and below (never above)
            }
            endZ = floor;         // shade drawn when we reach current floor
        }
        else
        {
            startZ = floor;
            endZ = floor;
            superEndZ = floor;
        }

        // ── Ghost higher floors: draw ONE floor above current with alpha=96 (~37%) ──
        if (ghostHigherFloors && floor > 0 && floor != 8)
        {
            byte upperFloor = (byte)(floor - 1);
            int ghostOffset;
            if (upperFloor <= 7)
                ghostOffset = (int)((7 - upperFloor) * TileSize * zoom);
            else
                ghostOffset = (int)((floor - upperFloor) * TileSize * zoom);

            using (context.PushOpacity(0.375)) // alpha=96/255 ≈ 0.375
            {
                for (int ty = startTileY; ty <= endTileY; ty++)
                {
                    for (int tx = startTileX; tx <= endTileX; tx++)
                    {
                        var pos = new MapPosition((ushort)tx, (ushort)ty, upperFloor);
                        if (!_mapData.Tiles.TryGetValue(pos, out var tile)) continue;
                        double baseScreenX = (tx * TileSize - _viewX) * zoom - ghostOffset;
                        double baseScreenY = (ty * TileSize - _viewY) * zoom - ghostOffset;
                        if (showAsMinimap)
                            DrawMinimapTile(context, tile, baseScreenX, baseScreenY, tilePixelSize);
                        else
                            foreach (var item in tile.Items)
                                DrawItem(context, ResolveClientId(item.Id), pos,
                                         baseScreenX, baseScreenY, zoom, 0, 1.0);
                    }
                }
            }
        }

        // ── Multi-floor loop: render from startZ down to superEndZ ──
        // Reference: for(map_z = start_z; map_z >= superend_z; map_z--)
        // Shade is drawn ONCE when map_z reaches end_z (current floor) and start_z != end_z
        for (int mapZ = startZ; mapZ >= superEndZ; mapZ--)
        {
            bool isCurrentFloor = (mapZ == floor);

            // Shade: single 50% black overlay between lower floors and current floor
            // Reference: if(map_z == end_z && start_z != end_z && options.show_shade)
            if (mapZ == endZ && startZ != endZ && showShade)
            {
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)), null,
                    new Rect(0, 0, bw, bh));
            }

            // Per-floor coordinate offset (isometric stacking)
            // Reference: surface: offset = (GROUND_LAYER - map_z) * TileSize
            //            underground: offset = TileSize * (floor - map_z)
            double floorOffset;
            if (mapZ <= 7)
                floorOffset = (7 - mapZ) * TileSize * zoom;
            else
                floorOffset = (floor - mapZ) * TileSize * zoom;

            for (int ty = startTileY; ty <= endTileY; ty++)
            {
                for (int tx = startTileX; tx <= endTileX; tx++)
                {
                    var pos = new MapPosition((ushort)tx, (ushort)ty, (byte)mapZ);
                    if (!_mapData.Tiles.TryGetValue(pos, out var tile)) continue;

                    double baseScreenX = (tx * TileSize - _viewX) * zoom - floorOffset;
                    double baseScreenY = (ty * TileSize - _viewY) * zoom - floorOffset;

                    // ── Minimap mode ──
                    if (showAsMinimap)
                    {
                        if (isCurrentFloor) // only draw minimap for current floor
                            DrawMinimapTile(context, tile, baseScreenX, baseScreenY, tilePixelSize);
                        continue;
                    }

                    // Track cumulative elevation offset
                    double elevationOffsetX = 0;
                    double elevationOffsetY = 0;
                    bool isFirstItem = true;

                    // Pre-compute zone/stair overlay color (only for current floor)
                    Color? groundOverlayColor = null;
                    if (isCurrentFloor)
                    {
                        if (showSpecial && tile.Flags != 0)
                        {
                            if ((tile.Flags & 0x01) != 0)
                                groundOverlayColor = Color.FromArgb(100, 0, 255, 0);
                            else if ((tile.Flags & 0x04) != 0)
                                groundOverlayColor = Color.FromArgb(100, 0, 200, 255);
                            else if ((tile.Flags & 0x02) != 0)
                                groundOverlayColor = Color.FromArgb(100, 255, 255, 0);
                            else if ((tile.Flags & 0x08) != 0)
                                groundOverlayColor = Color.FromArgb(100, 255, 0, 0);
                        }
                        if (groundOverlayColor == null && showSpecial && _serverToOtbItemMap != null)
                        {
                            const OtbFlags floorChangeMask = OtbFlags.FloorChangeDown | OtbFlags.FloorChangeNorth
                                | OtbFlags.FloorChangeEast | OtbFlags.FloorChangeSouth | OtbFlags.FloorChangeWest;
                            foreach (var tItem in tile.Items)
                            {
                                if (_serverToOtbItemMap.TryGetValue(tItem.Id, out var otbItem)
                                    && (otbItem.Flags & floorChangeMask) != 0)
                                {
                                    groundOverlayColor = Color.FromArgb(100, 255, 255, 0);
                                    break;
                                }
                            }
                        }
                    }

                    foreach (var item in tile.Items)
                    {
                        ushort clientId = ResolveClientId(item.Id);

                        // Ghost loose items: non-ground items at half opacity
                        double itemOpacity = 1.0;
                        if (ghostItems && !isFirstItem)
                            itemOpacity = 0.5;

                        DrawItem(context, clientId, pos, baseScreenX + elevationOffsetX,
                                 baseScreenY + elevationOffsetY, zoom, elapsedMs, itemOpacity);

                        if (_datData.Items.TryGetValue(clientId, out var thing))
                        {
                            if (thing.HasElevation)
                            {
                                elevationOffsetX -= thing.Elevation * zoom;
                                elevationOffsetY -= thing.Elevation * zoom;
                            }
                            if (isCurrentFloor && lights != null && thing.HasLight && thing.LightLevel > 0)
                                lights.Add((tx, ty, (byte)Math.Min((int)thing.LightLevel, MaxLightIntensity), (byte)thing.LightColor));

                            // Draw zone/stair overlay right after FullGround, so items above keep normal color
                            if (isFirstItem && groundOverlayColor.HasValue && thing.IsFullGround)
                            {
                                var tileRect = new Rect(baseScreenX, baseScreenY, tilePixelSize, tilePixelSize);
                                context.DrawRectangle(new SolidColorBrush(groundOverlayColor.Value), null, tileRect);
                                groundOverlayColor = null; // already drawn
                            }
                        }
                        isFirstItem = false;
                    }

                    // Fallback: if ground wasn't FullGround but overlay applies, draw it now
                    if (groundOverlayColor.HasValue)
                    {
                        var tileRect = new Rect(baseScreenX, baseScreenY, tilePixelSize, tilePixelSize);
                        context.DrawRectangle(new SolidColorBrush(groundOverlayColor.Value), null, tileRect);
                    }

                }
            }
        }

        // ── Tile overlays pass (drawn AFTER all items to avoid multi-tile sprites covering them) ──
        if (!showAsMinimap)
        {
            // Compute floor offset for current floor
            double overlayFloorOffset;
            if (floor <= 7)
                overlayFloorOffset = (7 - floor) * TileSize * zoom;
            else
                overlayFloorOffset = 0; // current floor offset is always 0 for itself

            for (int ty = startTileY; ty <= endTileY; ty++)
            {
                for (int tx = startTileX; tx <= endTileX; tx++)
                {
                    var pos = new MapPosition((ushort)tx, (ushort)ty, floor);
                    if (!_mapData.Tiles.TryGetValue(pos, out var tile)) continue;

                    double baseScreenX = (tx * TileSize - _viewX) * zoom - overlayFloorOffset;
                    double baseScreenY = (ty * TileSize - _viewY) * zoom - overlayFloorOffset;
                    var tileRect = new Rect(baseScreenX, baseScreenY, tilePixelSize, tilePixelSize);

                    // Show houses
                    if (showHouses && tile.HouseId > 0)
                        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(50, 64, 64, 255)), null, tileRect);

                    // Show pathing/blocking
                    if (showPathing)
                    {
                        bool isBlocking = false;
                        foreach (var item in tile.Items)
                        {
                            ushort cid = ResolveClientId(item.Id);
                            if (_datData.Items.TryGetValue(cid, out var dt) && dt.IsUnpassable)
                            { isBlocking = true; break; }
                        }
                        if (isBlocking)
                            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(80, 255, 80, 0)), null, tileRect);
                    }

                    // Highlight items (tint by item count)
                    if (highlightItems && tile.Items.Count > 1)
                    {
                        int count = tile.Items.Count - 1; // exclude ground
                        byte alpha = count switch { 1 => 30, 2 => 50, 3 => 70, 4 => 90, _ => 110 };
                        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(alpha, 255, 200, 0)), null, tileRect);
                    }
                }
            }
        }

        // ── Show waypoints ──
        if (showWaypoints && _mapData.Waypoints.Count > 0)
        {
            foreach (var wp in _mapData.Waypoints)
            {
                if (wp.Z != floor) continue;
                double sx = (wp.X * TileSize - _viewX) * zoom;
                double sy = (wp.Y * TileSize - _viewY) * zoom;
                // Blue diamond marker
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(180, 64, 64, 255)), null,
                    new Rect(sx + tilePixelSize * 0.25, sy + tilePixelSize * 0.25,
                             tilePixelSize * 0.5, tilePixelSize * 0.5));
                // Label
                if (zoom >= 0.5)
                {
                    var text = new FormattedText(wp.Name, System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Segoe UI", FontStyle.Normal, FontWeight.Bold),
                        9 * zoom, new SolidColorBrush(Color.FromRgb(130, 130, 255)));
                    context.DrawText(text, new Point(sx + tilePixelSize + 2, sy + 2));
                }
            }
        }

        // ── Show towns ──
        if (showTowns && _mapData.Towns.Count > 0)
        {
            foreach (var town in _mapData.Towns)
            {
                if (town.TempleZ != floor) continue;
                double sx = (town.TempleX * TileSize - _viewX) * zoom;
                double sy = (town.TempleY * TileSize - _viewY) * zoom;
                // Yellow diamond marker
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(200, 255, 255, 64)), null,
                    new Rect(sx + tilePixelSize * 0.2, sy + tilePixelSize * 0.2,
                             tilePixelSize * 0.6, tilePixelSize * 0.6));
                // Label
                if (zoom >= 0.3)
                {
                    var text = new FormattedText(town.Name, System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Segoe UI", FontStyle.Normal, FontWeight.Bold),
                        10 * Math.Max(zoom, 0.5), new SolidColorBrush(Color.FromRgb(255, 255, 100)));
                    context.DrawText(text, new Point(sx + tilePixelSize + 2, sy + 2));
                }
            }
        }

        // ── Light overlay ──
        if (lights != null && lights.Count > 0)
            DrawLightOverlay(context, lights, startTileX, startTileY, endTileX, endTileY, zoom);

        // ── Grid lines ──
        if (showGrid)
        {
            var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1);
            int gridStartX = (int)Math.Floor(_viewX / TileSize);
            int gridEndX = (int)Math.Ceiling((_viewX + bw / zoom) / TileSize);
            int gridStartY = (int)Math.Floor(_viewY / TileSize);
            int gridEndY = (int)Math.Ceiling((_viewY + bh / zoom) / TileSize);
            for (int tx = gridStartX; tx <= gridEndX; tx++)
            {
                double sx = (tx * TileSize - _viewX) * zoom;
                context.DrawLine(gridPen, new Point(sx, 0), new Point(sx, bh));
            }
            for (int ty = gridStartY; ty <= gridEndY; ty++)
            {
                double sy = (ty * TileSize - _viewY) * zoom;
                context.DrawLine(gridPen, new Point(0, sy), new Point(bw, sy));
            }
        }

        // ── Ingame box (15×11 tile viewport indicator) ──
        if (showIngameBox)
        {
            double boxW = 15 * tilePixelSize;
            double boxH = 11 * tilePixelSize;
            double boxX = (bw - boxW) / 2;
            double boxY = (bh - boxH) / 2;
            // Darken outside the box
            var shadowBrush = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0));
            context.DrawRectangle(shadowBrush, null, new Rect(0, 0, bw, boxY)); // top
            context.DrawRectangle(shadowBrush, null, new Rect(0, boxY + boxH, bw, bh - boxY - boxH)); // bottom
            context.DrawRectangle(shadowBrush, null, new Rect(0, boxY, boxX, boxH)); // left
            context.DrawRectangle(shadowBrush, null, new Rect(boxX + boxW, boxY, bw - boxX - boxW, boxH)); // right
            // Box border
            var boxPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 1);
            context.DrawRectangle(null, boxPen, new Rect(boxX, boxY, boxW, boxH));
        }

        // ── Ghost brush (translucent preview of selected item / zone at cursor) ──
        var ghostItemIds = BrushItemIds;
        bool hasGhostList = ghostItemIds != null && ghostItemIds.Count > 0;
        if (_cursorInsideCanvas && (BrushServerId > 0 || hasGhostList || ActiveZoneBrush > 0) && !_isDragging && !_isPasting && !_isSelecting && !_isMoving)
        {
            int floorTileOff = floor <= 7 ? 7 - floor : 0;
            int ghostTileX = (int)Math.Floor((_viewX + _cursorScreenPos.X / zoom) / TileSize) + floorTileOff;
            int ghostTileY = (int)Math.Floor((_viewY + _cursorScreenPos.Y / zoom) / TileSize) + floorTileOff;
            double floorOff = floor <= 7 ? (7 - floor) * TileSize * zoom : 0;

            var center = new MapPosition((ushort)Math.Clamp(ghostTileX, 0, 65535),
                                         (ushort)Math.Clamp(ghostTileY, 0, 65535), floor);
            var brushTiles = GetBrushTiles(center);

            if (BrushServerId > 0 || hasGhostList)
            {
                // For custom brush, show first item as preview
                ushort previewId = hasGhostList ? ghostItemIds![0] : BrushServerId;
                ushort brushClientId = ResolveClientId(previewId);
                using (context.PushOpacity(0.5))
                {
                    foreach (var pos in brushTiles)
                    {
                        double gx = (pos.X * TileSize - _viewX) * zoom - floorOff;
                        double gy = (pos.Y * TileSize - _viewY) * zoom - floorOff;
                        DrawItem(context, brushClientId, pos, gx, gy, zoom, elapsedMs, 1.0);
                    }
                }
            }
            else if (ActiveZoneBrush > 0)
            {
                // Show zone color preview
                Color zoneColor = ActiveZoneBrush switch
                {
                    1 => Color.FromArgb(80, 0, 255, 0),    // PZ
                    4 => Color.FromArgb(80, 0, 200, 255),   // NoPvP
                    2 => Color.FromArgb(80, 255, 255, 0),   // NoLogout
                    8 => Color.FromArgb(80, 255, 0, 0),     // PvPZone
                    _ => Color.FromArgb(60, 255, 255, 255),
                };
                var zoneBrush = new SolidColorBrush(zoneColor);
                foreach (var pos in brushTiles)
                {
                    double gx = (pos.X * TileSize - _viewX) * zoom - floorOff;
                    double gy = (pos.Y * TileSize - _viewY) * zoom - floorOff;
                    context.DrawRectangle(zoneBrush, null, new Rect(gx, gy, tilePixelSize, tilePixelSize));
                }
            }
        }

        // ── Selection highlight (tint selected tiles) ──
        if (_selectedTiles.Count > 0)
        {
            var selBrush = new SolidColorBrush(Color.FromArgb(60, 137, 180, 250)); // accent blue
            double floorOffset = floor <= 7 ? (7 - floor) * TileSize * zoom : 0;
            foreach (var pos in _selectedTiles)
            {
                if (pos.Z != floor) continue;
                double sx = (pos.X * TileSize - _viewX) * zoom - floorOffset;
                double sy = (pos.Y * TileSize - _viewY) * zoom - floorOffset;
                context.DrawRectangle(selBrush, null, new Rect(sx, sy, tilePixelSize, tilePixelSize));
            }
        }

        // ── Selection box / area fill preview (while dragging) ──
        if (_isSelecting)
        {
            var (minPos, maxPos) = GetSelectionRect();
            double floorOffset = floor <= 7 ? (7 - floor) * TileSize * zoom : 0;
            double rx1 = (minPos.X * TileSize - _viewX) * zoom - floorOffset;
            double ry1 = (minPos.Y * TileSize - _viewY) * zoom - floorOffset;
            double rx2 = ((maxPos.X + 1) * TileSize - _viewX) * zoom - floorOffset;
            double ry2 = ((maxPos.Y + 1) * TileSize - _viewY) * zoom - floorOffset;

            // If brush active, show translucent fill preview
            if (BrushServerId > 0 || hasGhostList)
            {
                ushort previewFillId = hasGhostList ? ghostItemIds![0] : BrushServerId;
                ushort brushClientId = ResolveClientId(previewFillId);
                using (context.PushOpacity(0.35))
                {
                    for (int y = minPos.Y; y <= maxPos.Y; y++)
                        for (int x = minPos.X; x <= maxPos.X; x++)
                        {
                            double gx = (x * TileSize - _viewX) * zoom - floorOffset;
                            double gy = (y * TileSize - _viewY) * zoom - floorOffset;
                            var pos = new MapPosition((ushort)x, (ushort)y, floor);
                            DrawItem(context, brushClientId, pos, gx, gy, zoom, elapsedMs, 1.0);
                        }
                }
            }
            else if (ActiveZoneBrush > 0)
            {
                Color zoneColor = ActiveZoneBrush switch
                {
                    1 => Color.FromArgb(60, 0, 255, 0),
                    4 => Color.FromArgb(60, 0, 200, 255),
                    2 => Color.FromArgb(60, 255, 255, 0),
                    8 => Color.FromArgb(60, 255, 0, 0),
                    _ => Color.FromArgb(40, 255, 255, 255),
                };
                context.DrawRectangle(new SolidColorBrush(zoneColor), null,
                    new Rect(rx1, ry1, rx2 - rx1, ry2 - ry1));
            }

            var selBoxPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1,
                new DashStyle(new double[] { 4, 4 }, 0));
            context.DrawRectangle(null, selBoxPen, new Rect(rx1, ry1, rx2 - rx1, ry2 - ry1));
        }

        // ── Paste ghost preview (copy buffer contents at cursor offset) ──
        if (_isPasting && _copyBuffer != null && _cursorInsideCanvas)
        {
            int floorTileOff = floor <= 7 ? 7 - floor : 0;
            int cursorTileX = (int)Math.Floor((_viewX + _cursorScreenPos.X / zoom) / TileSize) + floorTileOff;
            int cursorTileY = (int)Math.Floor((_viewY + _cursorScreenPos.Y / zoom) / TileSize) + floorTileOff;
            int offX = cursorTileX - _copyOrigin.X;
            int offY = cursorTileY - _copyOrigin.Y;
            double floorOffset = floor <= 7 ? (7 - floor) * TileSize * zoom : 0;

            using (context.PushOpacity(0.5))
            {
                foreach (var (pos, tile) in _copyBuffer)
                {
                    int drawTX = pos.X + offX;
                    int drawTY = pos.Y + offY;
                    double sx = (drawTX * TileSize - _viewX) * zoom - floorOffset;
                    double sy = (drawTY * TileSize - _viewY) * zoom - floorOffset;
                    var drawPos = new MapPosition((ushort)Math.Clamp(drawTX, 0, 65535),
                                                  (ushort)Math.Clamp(drawTY, 0, 65535), floor);
                    foreach (var item in tile.Items)
                        DrawItem(context, ResolveClientId(item.Id), drawPos, sx, sy, zoom, elapsedMs, 1.0);
                }
            }
        }

        // Center crosshair
        var crossPen = new Pen(new SolidColorBrush(Color.FromArgb(60, 137, 180, 250)), 1);
        context.DrawLine(crossPen, new Point(bw / 2.0, 0), new Point(bw / 2.0, bh));
        context.DrawLine(crossPen, new Point(0, bh / 2.0), new Point(bw, bh / 2.0));

        // Start/stop animation timer based on whether animated items are visible
        UpdateAnimationTimer();
    }

    /// <summary>Draws a tile as a flat-colored minimap square.</summary>
    private void DrawMinimapTile(DrawingContext context, MapTile tile,
                                  double screenX, double screenY, double tilePixelSize)
    {
        Color col;
        if (tile.Ground != null)
        {
            ushort groundClientId = ResolveClientId(tile.Ground.Id);
            if (_datData?.Items.TryGetValue(groundClientId, out var dt) == true && dt.IsMiniMap)
                col = GetMinimapColor(dt.MiniMapColor);
            else
                col = Color.FromRgb(0x6c, 0x71, 0x86);
        }
        else if (tile.Items.Count > 0)
            col = Color.FromRgb(0x6c, 0x71, 0x86);
        else
            col = Color.FromRgb(0x45, 0x47, 0x5a);

        context.DrawRectangle(new SolidColorBrush(col), null,
            new Rect(screenX, screenY, tilePixelSize, tilePixelSize));
    }

    /// <summary>
    /// Resolves a server item ID to a client ID via OTB mapping.
    /// </summary>
    private ushort ResolveClientId(ushort serverId)
    {
        if (_serverToClientMap != null && _serverToClientMap.TryGetValue(serverId, out var clientId))
            return clientId;
        return serverId;
    }

    /// <summary>
    /// Draws a single item at the given screen anchor position.
    /// Multi-tile items have their sub-sprites extend LEFT and UP from the anchor,
    /// matching the reference editor (RME) behavior.
    /// Displacement offsets from DAT are applied. Animation frame selected by elapsed time.
    /// </summary>
    private void DrawItem(DrawingContext context, ushort clientId, MapPosition pos,
                          double anchorScreenX, double anchorScreenY, double zoom,
                          long elapsedMs, double opacity = 1.0)
    {
        if (_datData == null || _sprFile == null) return;

        if (!_datData.Items.TryGetValue(clientId, out var thing) || thing.FrameGroups.Length == 0)
            return;

        var fg = thing.FrameGroups[0];
        if (fg.SpriteIndex.Length == 0) return;

        double tilePixelSize = TileSize * zoom;

        // Apply displacement offset from DAT (in pixels, scaled by zoom)
        double offsetX = thing.HasOffset ? -thing.OffsetX * zoom : 0;
        double offsetY = thing.HasOffset ? -thing.OffsetY * zoom : 0;

        // Position-based patterns (tiling like grass, water, walls)
        int patternX = fg.PatternX > 1 ? pos.X % fg.PatternX : 0;
        int patternY = fg.PatternY > 1 ? pos.Y % fg.PatternY : 0;
        int patternZ = fg.PatternZ > 1 ? pos.Z % fg.PatternZ : 0;

        // Animation frame calculation
        int frame = 0;
        if (fg.Frames > 1)
        {
            _hasAnimatedItems = true;
            frame = GetAnimationFrame(fg, elapsedMs);
        }

        // Wrap drawing in opacity push if needed
        IDisposable? opacityState = opacity < 1.0 ? context.PushOpacity(opacity) : null;

        // Draw each sub-sprite: cx extends LEFT, cy extends UP from anchor
        for (int cx = 0; cx < fg.Width; cx++)
        {
            for (int cy = 0; cy < fg.Height; cy++)
            {
                for (int layer = 0; layer < fg.Layers; layer++)
                {
                    uint sprId = fg.GetSpriteId(cx, cy, layer, patternX, patternY, patternZ, frame);
                    if (sprId == 0) continue;

                    var bmp = GetSpriteBitmap(sprId);
                    if (bmp == null) continue;

                    // Sub-sprite positioned LEFT and UP from anchor (like reference: screenx - cx * TileSize)
                    double drawX = anchorScreenX - cx * tilePixelSize + offsetX;
                    double drawY = anchorScreenY - cy * tilePixelSize + offsetY;

                    context.DrawImage(bmp, new Rect(drawX, drawY, tilePixelSize, tilePixelSize));
                }
            }
        }

        opacityState?.Dispose();
    }

    // ── Animation ──

    /// <summary>
    /// Calculates the current animation frame for a frame group based on elapsed time.
    /// Uses synchronous mode: all instances of the same item type show the same frame,
    /// matching the reference editor behavior.
    /// </summary>
    private static int GetAnimationFrame(FrameGroup fg, long elapsedMs)
    {
        if (fg.Frames <= 1) return 0;

        // Calculate total animation duration
        long totalDuration = 0;
        if (fg.FrameDurations.Length > 0)
        {
            for (int i = 0; i < fg.Frames; i++)
            {
                if (i < fg.FrameDurations.Length)
                    totalDuration += fg.FrameDurations[i].Minimum; // Use minimum for sync mode
                else
                    totalDuration += 500; // Default 500ms per frame (ITEM_FRAME_DURATION)
            }
        }
        else
        {
            totalDuration = fg.Frames * 500L;
        }

        if (totalDuration <= 0) return 0;

        // Synchronous: all sprites of the same type show the same frame based on global time
        long elapsed = elapsedMs % totalDuration;
        long accumulated = 0;
        for (int i = 0; i < fg.Frames; i++)
        {
            long frameDur = (i < fg.FrameDurations.Length) ? fg.FrameDurations[i].Minimum : 500;
            if (elapsed < accumulated + frameDur)
                return i;
            accumulated += frameDur;
        }
        return 0;
    }

    /// <summary>
    /// Starts or stops the animation timer depending on whether animated items are visible.
    /// Timer fires every 100ms (matching reference editor).
    /// </summary>
    private void UpdateAnimationTimer()
    {
        if (_hasAnimatedItems && _mapData != null)
        {
            if (_animationTimer == null)
            {
                _animationTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100),
                    DispatcherPriority.Render, (_, _) => InvalidateVisual());
                _animationTimer.Start();
            }
        }
        else
        {
            if (_animationTimer != null)
            {
                _animationTimer.Stop();
                _animationTimer = null;
            }
        }
    }

    // ── Light overlay ──

    /// <summary>
    /// Renders a semi-transparent dark overlay with bright circular areas around light sources.
    /// Uses the same algorithm as the reference editor: per-tile lightmap with distance-based falloff,
    /// rendered as a bitmap overlay with multiplicative-style blending (approximated via alpha).
    /// </summary>
    private void DrawLightOverlay(DrawingContext context,
        List<(int x, int y, byte intensity, byte color)> lights,
        int startX, int startY, int endX, int endY, double zoom)
    {
        int w = endX - startX + 1;
        int h = endY - startY + 1;
        if (w <= 0 || h <= 0 || w > 512 || h > 512) return;

        var pixels = new byte[w * h * 4];

        // Initialize with dark ambient (the "unlit" darkness)
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0;       // R
            pixels[i + 1] = 0;   // G
            pixels[i + 2] = 0;   // B
            pixels[i + 3] = 140; // Alpha (matches reference ambient)
        }

        // Accumulate lights using MAX blending per channel (same as reference)
        foreach (var (lx, ly, intensity, colorIdx) in lights)
        {
            var lightColor = ColorFromEightBit(colorIdx);
            int radius = intensity;

            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int mx = lx + dx - startX;
                    int my = ly + dy - startY;
                    if (mx < 0 || mx >= w || my < 0 || my >= h) continue;

                    // Distance-based falloff matching reference:
                    // intensity_factor = (-distance + intensity) * 0.2
                    double distance = Math.Sqrt(dx * dx + dy * dy);
                    if (distance > MaxLightIntensity) continue;
                    double factor = (-distance + intensity) * 0.2;
                    if (factor < 0.01) continue;
                    factor = Math.Min(factor, 1.0);

                    int idx = (my * w + mx) * 4;
                    byte r = (byte)(lightColor.R * factor);
                    byte g = (byte)(lightColor.G * factor);
                    byte b = (byte)(lightColor.B * factor);

                    // MAX blend: brighter light wins per channel
                    pixels[idx] = Math.Max(pixels[idx], r);
                    pixels[idx + 1] = Math.Max(pixels[idx + 1], g);
                    pixels[idx + 2] = Math.Max(pixels[idx + 2], b);

                    // Reduce alpha where lit (inverse of light brightness → transparency)
                    byte maxChannel = Math.Max(r, Math.Max(g, b));
                    byte litAlpha = (byte)(140 - (int)(maxChannel * 140.0 / 255.0));
                    pixels[idx + 3] = Math.Min(pixels[idx + 3], litAlpha);
                }
            }
        }

        // Create lightmap bitmap (1 pixel per tile, stretched over tile area)
        var lightBmp = new WriteableBitmap(
            new PixelSize(w, h), new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using (var fb = lightBmp.Lock())
            Marshal.Copy(pixels, 0, fb.Address, pixels.Length);

        // Draw stretched over the tile area with bilinear filtering (smooth gradients)
        double tilePixelSize = TileSize * zoom;
        double drawX = (startX * TileSize - _viewX) * zoom;
        double drawY = (startY * TileSize - _viewY) * zoom;
        double drawW = w * tilePixelSize;
        double drawH = h * tilePixelSize;

        // Use RenderOptions for bilinear filtering (smooth light circles)
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.LowQuality }))
        {
            context.DrawImage(lightBmp, new Rect(drawX, drawY, drawW, drawH));
        }
    }

    /// <summary>Converts an 8-bit color index (6×6×6 cube) to RGB, matching the reference.</summary>
    private static Color ColorFromEightBit(byte colorIndex)
    {
        if (colorIndex == 0 || colorIndex >= 216)
            return Color.FromRgb(255, 255, 255); // Default white for color 0 (natural light)
        byte r = (byte)(colorIndex / 36 % 6 * 51);
        byte g = (byte)(colorIndex / 6 % 6 * 51);
        byte b = (byte)(colorIndex % 6 * 51);
        return Color.FromRgb(r, g, b);
    }

    // ── Sprite bitmap cache (individual 32x32 sprites) ──

    /// <summary>
    /// Gets or creates a WriteableBitmap for a single 32x32 sprite by its SPR file ID.
    /// </summary>
    private WriteableBitmap? GetSpriteBitmap(uint spriteId)
    {
        if (_spriteBitmapCache.TryGetValue(spriteId, out var cached))
            return cached;

        if (!_spriteRgbaCache.TryGetValue(spriteId, out var rgba))
        {
            try { rgba = _sprFile!.GetSpriteRgba(spriteId); }
            catch (ObjectDisposedException) { return null; }
            _spriteRgbaCache[spriteId] = rgba;
        }

        if (rgba == null)
        {
            _spriteBitmapCache[spriteId] = null;
            return null;
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(TileSize, TileSize), new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using (var fb = bitmap.Lock())
            Marshal.Copy(rgba, 0, fb.Address, rgba.Length);

        _spriteBitmapCache[spriteId] = bitmap;
        return bitmap;
    }

    private void ClearCaches()
    {
        _spriteRgbaCache.Clear();
        _spriteBitmapCache.Clear();
    }

    // ── Minimap rendering ──

    private void RenderMinimap()
    {
        if (_mapData == null || _mapData.Tiles.Count == 0) return;
        byte floor = CurrentFloor;

        var (minX, minY, maxX, maxY) = _mapData.GetBounds();
        int w = maxX - minX + 1;
        int h = maxY - minY + 1;
        int bmpW = w * MiniMapTileSize;
        int bmpH = h * MiniMapTileSize;

        if (bmpW <= 0 || bmpH <= 0 || bmpW > 4096 || bmpH > 4096) return;

        var pixels = new byte[bmpW * bmpH * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0x11; pixels[i + 1] = 0x11; pixels[i + 2] = 0x1b; pixels[i + 3] = 0xFF;
        }

        foreach (var (pos, tile) in _mapData.Tiles)
        {
            if (pos.Z != floor) continue;
            int px = (pos.X - minX) * MiniMapTileSize;
            int py = (pos.Y - minY) * MiniMapTileSize;
            if (px < 0 || py < 0 || px + MiniMapTileSize > bmpW || py + MiniMapTileSize > bmpH) continue;

            Color col;
            if (tile.Ground != null)
            {
                ushort groundClientId = tile.Ground.Id;
                if (_serverToClientMap != null && _serverToClientMap.TryGetValue(tile.Ground.Id, out var mc))
                    groundClientId = mc;
                if (_datData?.Items.TryGetValue(groundClientId, out var dt) == true && dt.IsMiniMap)
                    col = GetMinimapColor(dt.MiniMapColor);
                else
                    col = Color.FromRgb(0x6c, 0x71, 0x86);
            }
            else if (tile.Items.Count > 0)
                col = Color.FromRgb(0x6c, 0x71, 0x86);
            else
                col = Color.FromRgb(0x45, 0x47, 0x5a);

            for (int dy = 0; dy < MiniMapTileSize; dy++)
            {
                for (int dx = 0; dx < MiniMapTileSize; dx++)
                {
                    int idx = ((py + dy) * bmpW + px + dx) * 4;
                    pixels[idx] = col.R; pixels[idx + 1] = col.G;
                    pixels[idx + 2] = col.B; pixels[idx + 3] = 0xFF;
                }
            }
        }

        var old = _minimapBitmap;
        _minimapBitmap = new WriteableBitmap(
            new PixelSize(bmpW, bmpH), new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using (var fb = _minimapBitmap.Lock())
            Marshal.Copy(pixels, 0, fb.Address, pixels.Length);
        RaisePropertyChanged(MinimapBitmapProperty, old, _minimapBitmap);
    }

    private static Color GetMinimapColor(ushort colorIndex)
    {
        if (colorIndex >= 216) return Color.FromRgb(0x45, 0x47, 0x5a);
        byte r = (byte)(((colorIndex / 36) % 6) * 51);
        byte g = (byte)(((colorIndex / 6) % 6) * 51);
        byte b = (byte)((colorIndex % 6) * 51);
        return Color.FromRgb(r, g, b);
    }

    // ── Navigation ──

    public void CenterOnMap()
    {
        if (_mapData == null) return;
        var (minX, minY, maxX, maxY) = _mapData.GetBounds();
        double centerX = (minX + maxX) / 2.0 * TileSize;
        double centerY = (minY + maxY) / 2.0 * TileSize;
        double zoom = ZoomLevel;
        _viewX = centerX - Bounds.Width / 2.0 / zoom;
        _viewY = centerY - Bounds.Height / 2.0 / zoom;
        InvalidateVisual();
    }

    public void GoToPosition(ushort x, ushort y, byte z)
    {
        CurrentFloor = z;
        double zoom = ZoomLevel;
        _viewX = x * TileSize - Bounds.Width / 2.0 / zoom;
        _viewY = y * TileSize - Bounds.Height / 2.0 / zoom;
        _minimapDirty = true;
        InvalidateVisual();
    }

    // ── Selection / Editing helpers ──

    /// <summary>Returns the min/max tile corners of the current selection rectangle.</summary>
    private (MapPosition min, MapPosition max) GetSelectionRect()
    {
        int x1 = Math.Min(_selectionStart.X, _selectionEnd.X);
        int y1 = Math.Min(_selectionStart.Y, _selectionEnd.Y);
        int x2 = Math.Max(_selectionStart.X, _selectionEnd.X);
        int y2 = Math.Max(_selectionStart.Y, _selectionEnd.Y);
        return (new MapPosition((ushort)x1, (ushort)y1, _selectionStart.Z),
                new MapPosition((ushort)x2, (ushort)y2, _selectionStart.Z));
    }

    /// <summary>Convert screen pixel position to tile MapPosition on current floor.</summary>
    private MapPosition ScreenToTile(Point screen)
    {
        double zoom = ZoomLevel;
        int floorTileOff = CurrentFloor <= 7 ? 7 - CurrentFloor : 0;
        int tx = (int)Math.Floor((_viewX + screen.X / zoom) / TileSize) + floorTileOff;
        int ty = (int)Math.Floor((_viewY + screen.Y / zoom) / TileSize) + floorTileOff;
        return new MapPosition((ushort)Math.Clamp(tx, 0, 65535),
                               (ushort)Math.Clamp(ty, 0, 65535), CurrentFloor);
    }

    /// <summary>
    /// Merge items from <paramref name="source"/> into <paramref name="dest"/> (otacademy Tile::merge).
    /// Ground replaces ground; all other items are stacked in draw-order.
    /// </summary>
    private void MergeTile(MapTile dest, MapTile source)
    {
        foreach (var item in source.Items)
        {
            ushort cid = ResolveClientId(item.Id);
            DatThingType? dtype = null;
            _datData?.Items.TryGetValue(cid, out dtype);

            if (dtype?.IsGround == true)
            {
                // Ground replaces ground
                for (int i = 0; i < dest.Items.Count; i++)
                {
                    ushort eCid = ResolveClientId(dest.Items[i].Id);
                    if (_datData?.Items.TryGetValue(eCid, out var eType) == true && eType.IsGround)
                    {
                        dest.Items[i] = item.Clone();
                        goto nextItem;
                    }
                }
                dest.Items.Insert(0, item.Clone());
            }
            else
            {
                // Stack: insert in correct draw-order position
                bool isOnBottom = dtype?.IsOnBottom == true || dtype?.IsGroundBorder == true;
                if (isOnBottom)
                {
                    int insertIdx = 0;
                    for (int i = 0; i < dest.Items.Count; i++)
                    {
                        ushort eCid = ResolveClientId(dest.Items[i].Id);
                        if (_datData?.Items.TryGetValue(eCid, out var eType) == true
                            && (eType.IsGround || eType.IsGroundBorder || eType.IsOnBottom))
                            insertIdx = i + 1;
                        else
                            break;
                    }
                    dest.Items.Insert(insertIdx, item.Clone());
                }
                else
                {
                    dest.Items.Add(item.Clone());
                }
            }
            nextItem:;
        }
    }

    /// <summary>
    /// Adds an item to a tile following brush/catalog placement rules:
    /// - Ground items REPLACE existing ground (Items[0] if IsGround).
    /// - OnBottom items with same type replace existing OnBottom of same kind.
    /// - Other items stack at the end.
    /// Used ONLY for brush/catalog placement, NOT for move/paste.
    /// </summary>
    private void AddItemToTile(MapTile tile, ushort serverId)
    {
        ushort clientId = ResolveClientId(serverId);
        DatThingType? itemType = null;
        _datData?.Items.TryGetValue(clientId, out itemType);

        bool isGround = itemType?.IsGround == true;
        bool isOnBottom = itemType?.IsOnBottom == true || itemType?.IsGroundBorder == true;

        if (isGround)
        {
            // Replace existing ground: find first ground item and replace it
            for (int i = 0; i < tile.Items.Count; i++)
            {
                ushort existingClientId = ResolveClientId(tile.Items[i].Id);
                if (_datData?.Items.TryGetValue(existingClientId, out var existingType) == true
                    && existingType.IsGround)
                {
                    tile.Items[i] = new MapItem { Id = serverId };
                    return;
                }
            }
            // No existing ground found — insert at position 0
            tile.Items.Insert(0, new MapItem { Id = serverId });
        }
        else if (isOnBottom)
        {
            // RAW_LIKE_SIMONE: remove existing OnBottom items of the same sub-type,
            // then insert sorted among OnBottom items (after ground, before regular)
            tile.Items.RemoveAll(existing =>
            {
                ushort eCid = ResolveClientId(existing.Id);
                if (_datData?.Items.TryGetValue(eCid, out var eType) != true || eType is null) return false;
                // Remove if same OnBottom/GroundBorder category
                return (eType.IsOnBottom || eType.IsGroundBorder) && !eType.IsGround;
            });

            // Find insertion point: after ground items, before regular items
            int insertIdx = 0;
            for (int i = 0; i < tile.Items.Count; i++)
            {
                ushort eCid = ResolveClientId(tile.Items[i].Id);
                if (_datData?.Items.TryGetValue(eCid, out var eType) == true
                    && (eType.IsGround || eType.IsGroundBorder || eType.IsOnBottom))
                    insertIdx = i + 1;
                else
                    break;
            }
            tile.Items.Insert(insertIdx, new MapItem { Id = serverId });
        }
        else
        {
            // Regular item — stack at end
            tile.Items.Add(new MapItem { Id = serverId });
        }
    }

    /// <summary>
    /// If the given tile has a brush-system ground, recalculate borders for it + 8 neighbours.
    /// </summary>
    private void TryBorderize(MapPosition pos)
    {
        if (BrushDb == null || _mapData == null) return;
        if (!_mapData.Tiles.TryGetValue(pos, out var tile) || tile.Items.Count == 0) return;
        if (BrushDb.GetBrushForItem(tile.Items[0].Id) != null)
            BrushDb.Borderize(_mapData, pos);
    }

    /// <summary>Pre-snapshot the 8 neighbours into an undo dict (for borderize modifications).</summary>
    private void SnapshotNeighbours(MapPosition pos, Dictionary<MapPosition, MapTile?> snapshot)
    {
        if (_mapData == null) return;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = pos.X + dx, ny = pos.Y + dy;
                if (nx < 0 || nx > 65535 || ny < 0 || ny > 65535) continue;
                var nPos = new MapPosition((ushort)nx, (ushort)ny, pos.Z);
                if (!snapshot.ContainsKey(nPos))
                    snapshot[nPos] = _mapData.Tiles.TryGetValue(nPos, out var t) ? t.Clone() : null;
            }
    }

    // Brush size → radius: 0→0, 1→1, 2→2, 3→3, 4→4, 5→5, 6→6 (diameters 1,3,5,7,9,11,13)
    private static readonly int[] BrushRadii = [0, 1, 2, 3, 4, 5, 6];

    /// <summary>
    /// Returns the list of tile positions affected by the brush centred at <paramref name="center"/>.
    /// Uses BrushSize and BrushCircle styled properties.
    /// </summary>
    private List<MapPosition> GetBrushTiles(MapPosition center)
    {
        int idx = Math.Clamp(BrushSize, 0, BrushRadii.Length - 1);
        int radius = BrushRadii[idx];
        var result = new List<MapPosition>();

        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (BrushCircle && dx * dx + dy * dy > radius * radius)
                    continue;
                int nx = center.X + dx, ny = center.Y + dy;
                if (nx < 0 || nx > 65535 || ny < 0 || ny > 65535) continue;
                result.Add(new MapPosition((ushort)nx, (ushort)ny, center.Z));
            }

        return result;
    }

    /// <summary>Paint the current brush at the given center position using BrushSize.</summary>
    private void PaintBrushAt(MapPosition center, Dictionary<MapPosition, MapTile?> undoSnapshot)
    {
        if (_mapData == null) return;
        var itemIds = BrushItemIds;
        bool hasList = itemIds != null && itemIds.Count > 0;
        if (!hasList && BrushServerId == 0) return;

        var tiles = GetBrushTiles(center);
        foreach (var pos in tiles)
        {
            if (!undoSnapshot.ContainsKey(pos))
                undoSnapshot[pos] = _mapData.Tiles.TryGetValue(pos, out var ex) ? ex.Clone() : null;
            SnapshotNeighbours(pos, undoSnapshot);

            if (!_mapData.Tiles.TryGetValue(pos, out var tile))
            {
                tile = new MapTile { Position = pos };
                _mapData.Tiles[pos] = tile;
            }

            ushort idToPlace = hasList ? itemIds![_brushRandom.Next(itemIds.Count)] : BrushServerId;
            AddItemToTile(tile, idToPlace);
        }

        BatchBorderize(tiles);
    }

    /// <summary>Paint zone flags at the given center position using BrushSize.</summary>
    private void PaintZoneAt(MapPosition center, int zoneFlag, Dictionary<MapPosition, MapTile?> undoSnapshot)
    {
        if (_mapData == null) return;

        // All zone flags that we toggle
        const uint allZoneFlags = 0x01 | 0x02 | 0x04 | 0x08;
        var tiles = GetBrushTiles(center);

        foreach (var pos in tiles)
        {
            if (!undoSnapshot.ContainsKey(pos))
                undoSnapshot[pos] = _mapData.Tiles.TryGetValue(pos, out var ex) ? ex.Clone() : null;

            if (!_mapData.Tiles.TryGetValue(pos, out var tile))
            {
                if (zoneFlag == 0) continue; // clearing zone on non-existent tile is a no-op
                tile = new MapTile { Position = pos };
                _mapData.Tiles[pos] = tile;
            }

            // Clear all zone flags, then set the requested one
            tile.Flags = (uint)((tile.Flags & ~allZoneFlags) | (uint)zoneFlag);
        }
    }

    /// <summary>
    /// Borderize a set of positions + their 8-neighbours (de-duplicated).
    /// Used after area fill / batch paint.
    /// </summary>
    private void BatchBorderize(IEnumerable<MapPosition> positions)
    {
        if (BrushDb == null || _mapData == null) return;
        var todo = new HashSet<MapPosition>();
        foreach (var p in positions)
        {
            todo.Add(p);
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = p.X + dx, ny = p.Y + dy;
                    if (nx >= 0 && nx <= 65535 && ny >= 0 && ny <= 65535)
                        todo.Add(new MapPosition((ushort)nx, (ushort)ny, p.Z));
                }
        }
        foreach (var pos in todo)
        {
            if (!_mapData.Tiles.TryGetValue(pos, out var tile) || tile.Items.Count == 0) continue;
            if (BrushDb.GetBrushForItem(tile.Items[0].Id) == null) continue;

            // Remove existing border items
            for (int i = tile.Items.Count - 1; i >= 1; i--)
                if (BrushDb.BorderItemIds.Contains(tile.Items[i].Id))
                    tile.Items.RemoveAt(i);

            // Compute and insert new borders
            var borderIds = BrushDb.ComputeBorders(_mapData, pos);
            int insertIdx = 1;
            foreach (var id in borderIds)
            {
                tile.Items.Insert(insertIdx, new MapItem { Id = id });
                insertIdx++;
            }
        }
    }

    /// <summary>Fills a rectangular area with the current brush item.</summary>
    private void FillArea(MapPosition min, MapPosition max)
    {
        if (_mapData == null) return;
        var itemIds = BrushItemIds;
        bool hasList = itemIds != null && itemIds.Count > 0;
        if (!hasList && BrushServerId == 0) return;

        // Collect all positions for undo (including border zone for borderize)
        var positions = new List<MapPosition>();
        for (int y = min.Y; y <= max.Y; y++)
            for (int x = min.X; x <= max.X; x++)
                positions.Add(new MapPosition((ushort)x, (ushort)y, CurrentFloor));

        // Snapshot includes border neighbours
        var undoPositions = new HashSet<MapPosition>(positions);
        foreach (var p in positions)
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = p.X + dx, ny = p.Y + dy;
                    if (nx >= 0 && nx <= 65535 && ny >= 0 && ny <= 65535)
                        undoPositions.Add(new MapPosition((ushort)nx, (ushort)ny, p.Z));
                }
        PushUndo(undoPositions);

        foreach (var pos in positions)
        {
            if (!_mapData.Tiles.TryGetValue(pos, out var tile))
            {
                tile = new MapTile { Position = pos };
                _mapData.Tiles[pos] = tile;
            }
            ushort idToPlace = hasList ? itemIds![_brushRandom.Next(itemIds.Count)] : BrushServerId;
            AddItemToTile(tile, idToPlace);
        }

        BatchBorderize(positions);

        int count = (max.X - min.X + 1) * (max.Y - min.Y + 1);
        _minimapDirty = true;
        MapEdited?.Invoke();
        ActionLogged?.Invoke($"Area filled: {count} tiles");
        InvalidateVisual();
    }

    /// <summary>Fills a rectangular area with zone flags.</summary>
    private void FillZoneArea(MapPosition min, MapPosition max, int zoneFlag)
    {
        if (_mapData == null) return;

        const uint allZoneFlags = 0x01 | 0x02 | 0x04 | 0x08;
        var positions = new List<MapPosition>();
        for (int y = min.Y; y <= max.Y; y++)
            for (int x = min.X; x <= max.X; x++)
                positions.Add(new MapPosition((ushort)x, (ushort)y, CurrentFloor));

        PushUndo(positions);

        foreach (var pos in positions)
        {
            if (!_mapData.Tiles.TryGetValue(pos, out var tile))
            {
                if (zoneFlag == 0) continue;
                tile = new MapTile { Position = pos };
                _mapData.Tiles[pos] = tile;
            }
            tile.Flags = (uint)((tile.Flags & ~allZoneFlags) | (uint)zoneFlag);
        }

        int count = positions.Count;
        _minimapDirty = true;
        MapEdited?.Invoke();
        ActionLogged?.Invoke($"Zone area filled: {count} tiles with flag 0x{zoneFlag:X2}");
        InvalidateVisual();
    }

    /// <summary>Snapshot the given tiles for undo. Null means tile didn't exist.</summary>
    private void PushUndo(IEnumerable<MapPosition> positions)
    {
        if (_mapData == null) return;
        var snapshot = new Dictionary<MapPosition, MapTile?>();
        foreach (var pos in positions)
            snapshot[pos] = _mapData.Tiles.TryGetValue(pos, out var t) ? t.Clone() : null;
        if (snapshot.Count == 0) return;
        _undoStack.Push(snapshot);
        _redoStack.Clear();
        // Limit stack size
        if (_undoStack.Count > MaxUndoSteps)
        {
            var temp = new Stack<Dictionary<MapPosition, MapTile?>>(
                _undoStack.Reverse().Skip(_undoStack.Count - MaxUndoSteps));
            _undoStack.Clear();
            foreach (var item in temp.Reverse()) _undoStack.Push(item);
        }
    }

    private void Undo()
    {
        if (_undoStack.Count == 0 || _mapData == null) return;
        var snapshot = _undoStack.Pop();
        // Save current state for redo
        var redoSnap = new Dictionary<MapPosition, MapTile?>();
        foreach (var (pos, _) in snapshot)
            redoSnap[pos] = _mapData.Tiles.TryGetValue(pos, out var t) ? t.Clone() : null;
        _redoStack.Push(redoSnap);
        // Restore
        foreach (var (pos, tile) in snapshot)
        {
            if (tile != null) _mapData.Tiles[pos] = tile;
            else _mapData.Tiles.Remove(pos);
        }
        _minimapDirty = true;
        ActionLogged?.Invoke($"Undo: {snapshot.Count} tiles restored");
        MapEdited?.Invoke();
        InvalidateVisual();
    }

    private void Redo()
    {
        if (_redoStack.Count == 0 || _mapData == null) return;
        var snapshot = _redoStack.Pop();
        var undoSnap = new Dictionary<MapPosition, MapTile?>();
        foreach (var (pos, _) in snapshot)
            undoSnap[pos] = _mapData.Tiles.TryGetValue(pos, out var t) ? t.Clone() : null;
        _undoStack.Push(undoSnap);
        foreach (var (pos, tile) in snapshot)
        {
            if (tile != null) _mapData.Tiles[pos] = tile;
            else _mapData.Tiles.Remove(pos);
        }
        _minimapDirty = true;
        ActionLogged?.Invoke($"Redo: {snapshot.Count} tiles restored");
        MapEdited?.Invoke();
        InvalidateVisual();
    }

    private void CopySelection()
    {
        if (_selectedTiles.Count == 0 || _mapData == null) return;
        _copyBuffer = new Dictionary<MapPosition, MapTile>();
        int minX = int.MaxValue, minY = int.MaxValue;
        foreach (var pos in _selectedTiles)
        {
            if (pos.X < minX) minX = pos.X;
            if (pos.Y < minY) minY = pos.Y;
            if (_mapData.Tiles.TryGetValue(pos, out var tile))
                _copyBuffer[pos] = tile.Clone();
        }
        _copyOrigin = new MapPosition((ushort)minX, (ushort)minY, CurrentFloor);
        ActionLogged?.Invoke($"Copiados {_copyBuffer.Count} tiles");
    }

    private void PasteAtCursor()
    {
        if (_copyBuffer == null || _copyBuffer.Count == 0 || _mapData == null) return;
        int cursorTileX = (int)Math.Floor((_viewX + _cursorScreenPos.X / ZoomLevel) / TileSize);
        int cursorTileY = (int)Math.Floor((_viewY + _cursorScreenPos.Y / ZoomLevel) / TileSize);
        int offX = cursorTileX - _copyOrigin.X;
        int offY = cursorTileY - _copyOrigin.Y;

        // Collect all affected positions for undo
        var affected = new List<MapPosition>();
        foreach (var (pos, _) in _copyBuffer)
        {
            var dest = new MapPosition((ushort)Math.Clamp(pos.X + offX, 0, 65535),
                                       (ushort)Math.Clamp(pos.Y + offY, 0, 65535), CurrentFloor);
            affected.Add(dest);
        }
        PushUndo(affected);

        // Place tiles: if source has ground → replace dest tile; otherwise merge/stack
        _selectedTiles.Clear();
        foreach (var (pos, tile) in _copyBuffer)
        {
            var dest = new MapPosition((ushort)Math.Clamp(pos.X + offX, 0, 65535),
                                       (ushort)Math.Clamp(pos.Y + offY, 0, 65535), CurrentFloor);

            bool sourceHasGround = false;
            if (tile.Items.Count > 0)
            {
                ushort cid = ResolveClientId(tile.Items[0].Id);
                if (_datData?.Items.TryGetValue(cid, out var dtype) == true)
                    sourceHasGround = dtype.IsGround;
            }

            if (sourceHasGround)
            {
                // Full replace: pasted tile takes over destination
                var newTile = new MapTile { Position = dest, Flags = tile.Flags, HouseId = tile.HouseId };
                foreach (var item in tile.Items) newTile.Items.Add(item.Clone());
                _mapData.Tiles[dest] = newTile;
            }
            else
            {
                // Merge/stack onto existing tile
                if (!_mapData.Tiles.TryGetValue(dest, out var destTile))
                {
                    destTile = new MapTile { Position = dest };
                    _mapData.Tiles[dest] = destTile;
                }
                MergeTile(destTile, tile);
            }
            _selectedTiles.Add(dest);
        }
        _isPasting = false;
        _minimapDirty = true;
        ActionLogged?.Invoke($"Pasted {_copyBuffer.Count} tiles");
        MapEdited?.Invoke();
        InvalidateVisual();
    }

    private void DeleteSelection()
    {
        if (_selectedTiles.Count == 0 || _mapData == null) return;
        int count = _selectedTiles.Count;
        PushUndo(_selectedTiles);

        if (_isAreaSelection)
        {
            // Area selection: remove entire tiles (all items)
            foreach (var pos in _selectedTiles)
                _mapData.Tiles.Remove(pos);
        }
        else
        {
            // Single-tile selection: remove only the top item (like otacademy)
            foreach (var pos in _selectedTiles.ToList())
            {
                if (!_mapData.Tiles.TryGetValue(pos, out var tile) || tile.Items.Count == 0)
                    continue;

                // Find the topmost non-meta item: last in list is topmost
                tile.Items.RemoveAt(tile.Items.Count - 1);

                // If tile has no items left, remove it from the map
                if (tile.Items.Count == 0)
                    _mapData.Tiles.Remove(pos);
            }
        }

        _selectedTiles.Clear();
        _minimapDirty = true;
        ActionLogged?.Invoke($"Deleted {count} tiles");
        MapEdited?.Invoke();
        SelectedTileChanged?.Invoke(null);
        InvalidateVisual();
    }

    private void MoveSelectionTo(MapPosition target)
    {
        if (_selectedTiles.Count == 0 || _mapData == null) return;
        int offX = target.X - _moveStart.X;
        int offY = target.Y - _moveStart.Y;
        if (offX == 0 && offY == 0) return;

        // Collect all affected positions (source + dest) for undo
        var affected = new List<MapPosition>();
        foreach (var pos in _selectedTiles)
        {
            affected.Add(pos);
            affected.Add(new MapPosition((ushort)Math.Clamp(pos.X + offX, 0, 65535),
                                         (ushort)Math.Clamp(pos.Y + offY, 0, 65535), CurrentFloor));
        }
        PushUndo(affected);

        // Extract items from source tiles.
        // Single-tile selection: move only the top item.
        // Area selection: move all non-ground items (ground stays behind).
        var movedItems = new Dictionary<MapPosition, List<MapItem>>();
        foreach (var pos in _selectedTiles)
        {
            if (!_mapData.Tiles.TryGetValue(pos, out var tile) || tile.Items.Count == 0) continue;

            var itemsToMove = new List<MapItem>();

            if (_isAreaSelection)
            {
                for (int i = tile.Items.Count - 1; i >= 0; i--)
                {
                    ushort cid = ResolveClientId(tile.Items[i].Id);
                    DatThingType? dtype = null;
                    _datData?.Items.TryGetValue(cid, out dtype);

                    if (dtype?.IsGround == true) continue;

                    itemsToMove.Insert(0, tile.Items[i].Clone());
                    tile.Items.RemoveAt(i);
                }
            }
            else
            {
                // Single tile: pick only the topmost item
                var topItem = tile.Items[^1];
                ushort cid = ResolveClientId(topItem.Id);
                DatThingType? dtype = null;
                _datData?.Items.TryGetValue(cid, out dtype);

                // If the top item is ground and there's nothing else, skip
                if (dtype?.IsGround == true && tile.Items.Count == 1) continue;

                // If top is ground, don't move it; otherwise move just the top item
                if (dtype?.IsGround != true)
                {
                    itemsToMove.Add(topItem.Clone());
                    tile.Items.RemoveAt(tile.Items.Count - 1);
                }
            }

            if (itemsToMove.Count > 0)
                movedItems[pos] = itemsToMove;

            if (tile.Items.Count == 0)
                _mapData.Tiles.Remove(pos);
        }

        // Place at destination using merge (items stack, no same-type replacement)
        _selectedTiles.Clear();
        foreach (var (src, items) in movedItems)
        {
            var dest = new MapPosition((ushort)Math.Clamp(src.X + offX, 0, 65535),
                                       (ushort)Math.Clamp(src.Y + offY, 0, 65535), CurrentFloor);

            if (!_mapData.Tiles.TryGetValue(dest, out var destTile))
            {
                destTile = new MapTile { Position = dest };
                _mapData.Tiles[dest] = destTile;
            }

            // Build a temporary tile from moved items for merge
            var tempTile = new MapTile { Position = dest };
            foreach (var item in items) tempTile.Items.Add(item);
            MergeTile(destTile, tempTile);

            _selectedTiles.Add(dest);
        }
        _minimapDirty = true;
        ActionLogged?.Invoke($"Moved items from {movedItems.Count} tiles ({offX:+#;-#;0}, {offY:+#;-#;0})");
        MapEdited?.Invoke();
        InvalidateVisual();
    }

    // ── Right-click context menu (otacademy-style) ──

    /// <summary>Fired when user selects "Select RAW" in the context menu, passing the server ID.</summary>
    public event Action<ushort>? SelectRawRequested;

    /// <summary>Fired when user selects "Lookup in Collection" in the context menu.</summary>
    public event Action<ushort>? LookupInCollectionRequested;

    private void ShowMapContextMenu(MapPosition tilePos, Point screenPos)
    {
        var menu = new ContextMenu();
        var hasTile = _mapData!.Tiles.TryGetValue(tilePos, out var tile);
        var hasSelection = _selectedTiles.Count > 0;

        // Top item info (for single-item operations)
        MapItem? topItem = null;
        DatThingType? topDatType = null;
        string? topName = null;
        ushort topServerId = 0;
        ushort topClientId = 0;
        if (hasTile && tile!.Items.Count > 0)
        {
            topItem = tile.Items[^1];
            topServerId = topItem.Id;
            topClientId = ResolveClientId(topServerId);
            _datData?.Items.TryGetValue(topClientId, out topDatType);
            // Try to get name from OTB
            if (_otbData != null)
            {
                var otbItem = _otbData.Items.FirstOrDefault(i => i.ServerId == topServerId);
                topName = otbItem?.Name;
            }
        }

        // ── Always-present items ──
        var cutItem = new MenuItem { Header = "Cut", InputGesture = new KeyGesture(Key.X, KeyModifiers.Meta) };
        cutItem.Click += (_, _) => { CopySelection(); DeleteSelection(); };
        cutItem.IsEnabled = hasSelection;
        menu.Items.Add(cutItem);

        var copyItem = new MenuItem { Header = "Copy", InputGesture = new KeyGesture(Key.C, KeyModifiers.Meta) };
        copyItem.Click += (_, _) => CopySelection();
        copyItem.IsEnabled = hasSelection;
        menu.Items.Add(copyItem);

        var copyPosItem = new MenuItem { Header = "Copy Position" };
        copyPosItem.Click += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync($"{{x = {tilePos.X}, y = {tilePos.Y}, z = {tilePos.Z}}}");
            ActionLogged?.Invoke($"Copied position ({tilePos.X}, {tilePos.Y}, {tilePos.Z})");
        };
        menu.Items.Add(copyPosItem);

        var pasteItem = new MenuItem { Header = "Paste", InputGesture = new KeyGesture(Key.V, KeyModifiers.Meta) };
        pasteItem.Click += (_, _) =>
        {
            if (_copyBuffer is { Count: > 0 })
            {
                _isPasting = true;
                InvalidateVisual();
            }
        };
        pasteItem.IsEnabled = _copyBuffer is { Count: > 0 };
        menu.Items.Add(pasteItem);

        var deleteItem = new MenuItem { Header = "Delete", InputGesture = new KeyGesture(Key.Delete) };
        deleteItem.Click += (_, _) =>
        {
            // If no selection, select this tile first then delete
            if (_selectedTiles.Count == 0 && hasTile)
            {
                _selectedTiles.Add(tilePos);
                _isAreaSelection = false;
            }
            DeleteSelection();
        };
        deleteItem.IsEnabled = hasSelection || hasTile;
        menu.Items.Add(deleteItem);

        // ── Single-tile item operations ──
        if (topItem != null)
        {
            menu.Items.Add(new Separator());

            var copyServerIdItem = new MenuItem { Header = $"Copy Server ID ({topServerId})" };
            copyServerIdItem.Click += async (_, _) =>
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null)
                    await clipboard.SetTextAsync(topServerId.ToString());
                ActionLogged?.Invoke($"Copied server ID {topServerId}");
            };
            menu.Items.Add(copyServerIdItem);

            var copyClientIdItem = new MenuItem { Header = $"Copy Client ID ({topClientId})" };
            copyClientIdItem.Click += async (_, _) =>
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null)
                    await clipboard.SetTextAsync(topClientId.ToString());
                ActionLogged?.Invoke($"Copied client ID {topClientId}");
            };
            menu.Items.Add(copyClientIdItem);

            if (!string.IsNullOrEmpty(topName))
            {
                var copyNameItem = new MenuItem { Header = $"Copy Name (\"{topName}\")" };
                copyNameItem.Click += async (_, _) =>
                {
                    var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                    if (clipboard != null)
                        await clipboard.SetTextAsync(topName!);
                    ActionLogged?.Invoke($"Copied name \"{topName}\"");
                };
                menu.Items.Add(copyNameItem);
            }

            menu.Items.Add(new Separator());

            // Select RAW — set this item as the active brush
            var selectRawItem = new MenuItem { Header = "Select RAW" };
            selectRawItem.Click += (_, _) =>
            {
                SetCurrentValue(BrushServerIdProperty, topServerId);
                SelectRawRequested?.Invoke(topServerId);
                ActionLogged?.Invoke($"Selected RAW brush: {topServerId}");
            };
            menu.Items.Add(selectRawItem);

            // Lookup in Collection — navigate to the sub-collection containing this item
            var lookupItem = new MenuItem { Header = "Lookup in Collection" };
            lookupItem.Click += (_, _) =>
            {
                LookupInCollectionRequested?.Invoke(topServerId);
            };
            menu.Items.Add(lookupItem);

            // Browse Field — select the tile and show in inspector
            var browseFieldItem = new MenuItem { Header = "Browse Tile" };
            browseFieldItem.Click += (_, _) =>
            {
                _selectedTiles.Clear();
                _selectedTiles.Add(tilePos);
                _isAreaSelection = false;
                SelectedTileChanged?.Invoke(tilePos);
                InvalidateVisual();
            };
            menu.Items.Add(browseFieldItem);
        }

        this.ContextMenu = menu;
        menu.Open(this);
    }

    // ── Input handling ──

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

        if (point.Properties.IsMiddleButtonPressed || point.Properties.IsRightButtonPressed)
        {
            // Right-click clears the brush (like RME: drawing mode → selection mode)
            if (point.Properties.IsRightButtonPressed && (BrushServerId > 0 || (BrushItemIds?.Count > 0) || ActiveZoneBrush > 0))
            {
                SetCurrentValue(BrushServerIdProperty, (ushort)0);
                SetCurrentValue(BrushItemIdsProperty, null);
                SetCurrentValue(ActiveZoneBrushProperty, 0);
                InvalidateVisual();
            }

            _isDragging = true;
            _rightClickWasDrag = false;
            _dragStart = point.Position;
            _dragViewStartX = _viewX;
            _dragViewStartY = _viewY;
            e.Handled = true;
            Cursor = new Cursor(StandardCursorType.Hand);
        }
        else if (point.Properties.IsLeftButtonPressed && _mapData != null)
        {
            var tilePos = ScreenToTile(point.Position);

            // 1) If pasting, confirm paste
            if (_isPasting)
            {
                PasteAtCursor();
                e.Handled = true;
            }
            // 2) Shift+click = area fill (with brush) or rectangle selection (no brush)
            else if (shift)
            {
                _isSelecting = true;
                _selectionStart = tilePos;
                _selectionEnd = tilePos;
                e.Handled = true;
            }
            // 3) Click on already-selected tile = start move
            else if (_selectedTiles.Contains(tilePos))
            {
                _isMoving = true;
                _moveStart = tilePos;
                e.Handled = true;
            }
            // 4) Zone brush (paint tile flags)
            else if (ActiveZoneBrush >= 0 && ActiveZoneBrush != 0)
            {
                _paintUndoSnapshot = new Dictionary<MapPosition, MapTile?>();
                PaintZoneAt(tilePos, ActiveZoneBrush, _paintUndoSnapshot);

                _isPainting = true;
                _isZonePainting = true;
                _lastPaintedTile = tilePos;
                _selectedTiles.Clear();
                _minimapDirty = true;
                MapEdited?.Invoke();
                ActionLogged?.Invoke($"Zone flag 0x{ActiveZoneBrush:X2} painted at ({tilePos.X}, {tilePos.Y}, {tilePos.Z})");
                InvalidateVisual();
                e.Handled = true;
            }
            // 5) Brush tool (place item + start drag-paint)
            else if (BrushServerId > 0 || (BrushItemIds?.Count > 0))
            {
                _paintUndoSnapshot = new Dictionary<MapPosition, MapTile?>();
                PaintBrushAt(tilePos, _paintUndoSnapshot);

                _isPainting = true;
                _isZonePainting = false;
                _lastPaintedTile = tilePos;
                _selectedTiles.Clear();
                _minimapDirty = true;
                MapEdited?.Invoke();
                ActionLogged?.Invoke($"Item painted at ({tilePos.X}, {tilePos.Y}, {tilePos.Z})");
                InvalidateVisual();
                e.Handled = true;
            }
            // 6) Click on empty = single tile select / deselect
            else
            {
                _selectedTiles.Clear();
                _isAreaSelection = false;
                if (_mapData.Tiles.ContainsKey(tilePos))
                {
                    _selectedTiles.Add(tilePos);
                    SelectedTileChanged?.Invoke(tilePos);
                }
                else
                {
                    SelectedTileChanged?.Invoke(null);
                }
                InvalidateVisual();
                e.Handled = true;
            }
        }
        Focus();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetCurrentPoint(this);
        double zoom = ZoomLevel;

        // Track cursor for ghost rendering
        _cursorScreenPos = point.Position;
        _cursorInsideCanvas = true;

        if (_isDragging)
        {
            var delta = point.Position - _dragStart;
            if (Math.Abs(delta.X) > 4 || Math.Abs(delta.Y) > 4)
                _rightClickWasDrag = true;
            _viewX = _dragViewStartX - delta.X / zoom;
            _viewY = _dragViewStartY - delta.Y / zoom;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // Drag-paint: brush continuously while left button held
        if (_isPainting && _mapData != null)
        {
            var paintPos = ScreenToTile(point.Position);
            if (paintPos != _lastPaintedTile)
            {
                if (_paintUndoSnapshot != null)
                {
                    if (_isZonePainting)
                        PaintZoneAt(paintPos, ActiveZoneBrush, _paintUndoSnapshot);
                    else if (BrushServerId > 0 || (BrushItemIds?.Count > 0))
                        PaintBrushAt(paintPos, _paintUndoSnapshot);
                }

                _lastPaintedTile = paintPos;
                _minimapDirty = true;
                MapEdited?.Invoke();
                InvalidateVisual();
            }
            e.Handled = true;
            return;
        }

        // Update selection box while dragging
        if (_isSelecting)
        {
            _selectionEnd = ScreenToTile(point.Position);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // Repaint for ghost brush / paste preview position update
        if (BrushServerId > 0 || (BrushItemIds?.Count > 0) || ActiveZoneBrush > 0 || _isPasting || _isMoving)
            InvalidateVisual();

        // Update hovered tile info
        if (_mapData != null)
        {
            var pos = ScreenToTile(point.Position);
            if (_mapData.Tiles.TryGetValue(pos, out var tile))
                HoveredTileInfo = $"X:{pos.X} Y:{pos.Y} Z:{pos.Z} | {tile.Items.Count} items";
            else
                HoveredTileInfo = $"X:{pos.X} Y:{pos.Y} Z:{pos.Z}";
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        // Stop drag-painting — push the entire batch as one undo entry
        if (_isPainting)
        {
            _isPainting = false;
            if (_paintUndoSnapshot is { Count: > 0 })
            {
                _undoStack.Push(_paintUndoSnapshot);
                _redoStack.Clear();
                if (_undoStack.Count > MaxUndoSteps)
                {
                    var temp = new Stack<Dictionary<MapPosition, MapTile?>>(
                        _undoStack.Reverse().Skip(_undoStack.Count - MaxUndoSteps));
                    _undoStack.Clear();
                    foreach (var item in temp.Reverse()) _undoStack.Push(item);
                }
                ActionLogged?.Invoke($"Batch paint completed ({_paintUndoSnapshot.Count} tiles)");
            }
            _paintUndoSnapshot = null;
        }

        if (_isDragging)
        {
            _isDragging = false;
            Cursor = Cursor.Default;

            // Right-click without drag → show context menu
            if (!_rightClickWasDrag && _mapData != null)
            {
                var tilePos = ScreenToTile(e.GetPosition(this));
                ShowMapContextMenu(tilePos, e.GetPosition(this));
            }

            e.Handled = true;
        }

        // Finalize rectangle selection or area fill
        if (_isSelecting)
        {
            _isSelecting = false;
            _selectionEnd = ScreenToTile(e.GetPosition(this));
            var (minPos, maxPos) = GetSelectionRect();

            // If a brush or zone brush is active, fill the area instead of selecting
            if (BrushServerId > 0 || (BrushItemIds?.Count > 0))
            {
                FillArea(minPos, maxPos);
                e.Handled = true;
            }
            else if (ActiveZoneBrush > 0)
            {
                FillZoneArea(minPos, maxPos, ActiveZoneBrush);
                e.Handled = true;
            }
            else
            {
                _selectedTiles.Clear();
                _isAreaSelection = true;
                for (int y = minPos.Y; y <= maxPos.Y; y++)
                    for (int x = minPos.X; x <= maxPos.X; x++)
                    {
                        var pos = new MapPosition((ushort)x, (ushort)y, CurrentFloor);
                        if (_mapData?.Tiles.ContainsKey(pos) == true)
                            _selectedTiles.Add(pos);
                    }
                // If exactly 1 tile selected, notify inspector
                if (_selectedTiles.Count == 1)
                    SelectedTileChanged?.Invoke(_selectedTiles.First());
                else
                    SelectedTileChanged?.Invoke(null);
                InvalidateVisual();
                e.Handled = true;
            }
        }

        // Finalize move
        if (_isMoving)
        {
            _isMoving = false;
            var target = ScreenToTile(e.GetPosition(this));
            MoveSelectionTo(target);
            e.Handled = true;
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _cursorInsideCanvas = false;
        if (BrushServerId > 0 || (BrushItemIds?.Count > 0) || ActiveZoneBrush > 0)
            InvalidateVisual();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _cursorInsideCanvas = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var mousePos = e.GetPosition(this);
        double zoom = ZoomLevel;

        // World position under mouse before zoom
        double worldX = _viewX + mousePos.X / zoom;
        double worldY = _viewY + mousePos.Y / zoom;

        // Zoom
        double factor = e.Delta.Y > 0 ? 1.25 : 0.8;
        ZoomLevel = zoom * factor;
        zoom = ZoomLevel;

        // Adjust view to keep world position under mouse
        _viewX = worldX - mousePos.X / zoom;
        _viewY = worldY - mousePos.Y / zoom;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double scrollAmount = 64 / ZoomLevel;
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0 ||
                    (e.KeyModifiers & KeyModifiers.Meta) != 0; // Cmd on macOS
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

        switch (e.Key)
        {
            case Key.Left: _viewX -= scrollAmount; InvalidateVisual(); e.Handled = true; break;
            case Key.Right: _viewX += scrollAmount; InvalidateVisual(); e.Handled = true; break;
            case Key.Up: _viewY -= scrollAmount; InvalidateVisual(); e.Handled = true; break;
            case Key.Down: _viewY += scrollAmount; InvalidateVisual(); e.Handled = true; break;
            case Key.PageUp:
                if (CurrentFloor > 0) CurrentFloor = (byte)(CurrentFloor - 1);
                e.Handled = true;
                break;
            case Key.PageDown:
                if (CurrentFloor < 15) CurrentFloor = (byte)(CurrentFloor + 1);
                e.Handled = true;
                break;
            case Key.Home:
                CenterOnMap();
                e.Handled = true;
                break;

            // ── Editing shortcuts ──
            case Key.C:
                if (ctrl) { CopySelection(); e.Handled = true; }
                break;
            case Key.V:
                if (ctrl)
                {
                    if (_copyBuffer != null && _copyBuffer.Count > 0)
                    {
                        _isPasting = true;
                        InvalidateVisual();
                    }
                    e.Handled = true;
                }
                else if (!shift) { HighlightItems = !HighlightItems; e.Handled = true; }
                break;
            case Key.X:
                if (ctrl) { CopySelection(); DeleteSelection(); e.Handled = true; }
                break;
            case Key.Z:
                if (ctrl && shift) { Redo(); e.Handled = true; }
                else if (ctrl) { Undo(); e.Handled = true; }
                break;
            case Key.S:
                if (ctrl) { SaveRequested?.Invoke(); e.Handled = true; }
                break;
            case Key.Delete:
            case Key.Back:
                DeleteSelection();
                e.Handled = true;
                break;
            case Key.Escape:
                if (_isPasting) { _isPasting = false; InvalidateVisual(); }
                else { _selectedTiles.Clear(); SelectedTileChanged?.Invoke(null); InvalidateVisual(); }
                e.Handled = true;
                break;
            case Key.A:
                // Ctrl+A = select all visible tiles on current floor
                if (ctrl && _mapData != null)
                {
                    _selectedTiles.Clear();
                    _isAreaSelection = true;
                    byte f = CurrentFloor;
                    foreach (var pos in _mapData.Tiles.Keys)
                        if (pos.Z == f) _selectedTiles.Add(pos);
                    InvalidateVisual();
                    e.Handled = true;
                }
                break;

            // Otacademy shortcuts (Ctrl = Cmd on macOS)
            case Key.W:
                if (ctrl) { ShowAllFloors = !ShowAllFloors; e.Handled = true; }
                else if (shift) { ShowWaypoints = !ShowWaypoints; e.Handled = true; }
                break;
            case Key.E:
                if (shift) { ShowAsMinimap = !ShowAsMinimap; e.Handled = true; }
                else if (!ctrl) { ShowSpecial = !ShowSpecial; e.Handled = true; }
                break;
            case Key.L:
                if (ctrl) { GhostHigherFloors = !GhostHigherFloors; e.Handled = true; }
                else if (shift) { ShowLights = !ShowLights; e.Handled = true; }
                else { ShowAnimation = !ShowAnimation; e.Handled = true; }
                break;
            case Key.G:
                if (shift) { ShowGrid = !ShowGrid; e.Handled = true; }
                else if (!ctrl) { GhostItems = !GhostItems; e.Handled = true; }
                break;
            case Key.I:
                if (shift) { ShowIngameBox = !ShowIngameBox; e.Handled = true; }
                break;
            case Key.H:
                if (ctrl) { ShowHouses = !ShowHouses; e.Handled = true; }
                break;
            case Key.Q:
                if (!ctrl && !shift) { ShowShade = !ShowShade; e.Handled = true; }
                break;
            case Key.Y:
                if (!ctrl && !shift) { ShowTooltips = !ShowTooltips; e.Handled = true; }
                break;
            case Key.O:
                if (!ctrl && !shift) { ShowPathing = !ShowPathing; e.Handled = true; }
                break;
        }
    }
}
