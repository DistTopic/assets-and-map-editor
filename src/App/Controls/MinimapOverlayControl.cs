using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AssetsAndMapEditor.OTB;

namespace AssetsAndMapEditor.App.Controls;

/// <summary>
/// Floating minimap overlay that shows the full map with a viewport rectangle.
/// Supports click-to-navigate, drag-to-pan, drag title bar to reposition, and drag corner to resize.
/// </summary>
public sealed class MinimapOverlayControl : Control
{
    private const double TitleBarHeight = 22;
    private const double ResizeGripSize = 10;
    private const double OverlayMinW = 120;
    private const double OverlayMinH = 100;
    private const double OverlayMaxW = 600;
    private const double OverlayMaxH = 500;
    private const int TileSize = 32; // MapCanvasControl.TileSize
    private const int MiniMapTileSize = 2; // pixels per tile in bitmap

    // Reference to the map canvas (set from code-behind)
    private MapCanvasControl? _mapCanvas;

    // Drag state
    private bool _isDraggingOverlay;
    private bool _isResizing;
    private bool _isNavigating;
    private Point _interactionStart;
    private double _startWidth, _startHeight;

    // Position offset via TranslateTransform
    private readonly TranslateTransform _translate = new();

    // Theme colors (Catppuccin Mocha)
    private static readonly Color BgColor = Color.FromArgb(230, 0x18, 0x18, 0x25);
    private static readonly Color TitleBgColor = Color.FromArgb(255, 0x1e, 0x1e, 0x2e);
    private static readonly Color BorderColor = Color.FromArgb(180, 0x31, 0x32, 0x44);
    private static readonly Color TitleTextColor = Color.FromRgb(0x58, 0x5b, 0x70);
    private static readonly Color ViewportRectColor = Color.FromArgb(200, 0x89, 0xb4, 0xfa);
    private static readonly Color ResizeGripColor = Color.FromArgb(140, 0x58, 0x5b, 0x70);

    public MinimapOverlayControl()
    {
        RenderTransform = _translate;
        Width = 220;
        Height = 180;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    /// <summary>Attach to a MapCanvasControl to read viewport and bitmap data.</summary>
    public void Attach(MapCanvasControl canvas)
    {
        _mapCanvas = canvas;
    }

    /// <summary>Called by the map canvas whenever it repaints, so we update too.</summary>
    public void OnMapCanvasRendered() =>
        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        double w = bounds.Width;
        double h = bounds.Height;
        if (w <= 0 || h <= 0) return;

        // Background
        context.DrawRectangle(
            new SolidColorBrush(BgColor),
            new Pen(new SolidColorBrush(BorderColor), 1),
            new Rect(0, 0, w, h), 6, 6);

        // Title bar
        context.DrawRectangle(
            new SolidColorBrush(TitleBgColor), null,
            new Rect(1, 1, w - 2, TitleBarHeight), 6, 6);
        // Bottom corners of title bar should be square (overlap with content area)
        context.DrawRectangle(
            new SolidColorBrush(TitleBgColor), null,
            new Rect(1, TitleBarHeight - 4, w - 2, 4));

        // Title text
        var titleText = new FormattedText("Minimap",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI", FontStyle.Normal, FontWeight.SemiBold),
            10, new SolidColorBrush(TitleTextColor));
        context.DrawText(titleText, new Point((w - titleText.Width) / 2, (TitleBarHeight - titleText.Height) / 2));

        // Content area for the minimap image
        double contentTop = TitleBarHeight + 2;
        double contentW = w - 8;   // 4px padding each side
        double contentH = h - contentTop - 6; // 6px bottom padding (2px extra for resize grip)
        double contentX = 4;
        double contentY = contentTop;

        if (contentW <= 0 || contentH <= 0) return;

        // Draw minimap bitmap if available
        var bitmap = _mapCanvas?.MinimapBitmap;
        if (bitmap != null)
        {
            double bmpW = bitmap.Size.Width;
            double bmpH = bitmap.Size.Height;
            if (bmpW > 0 && bmpH > 0)
            {
                // Scale to fit within content area maintaining aspect ratio
                double scale = Math.Min(contentW / bmpW, contentH / bmpH);
                double drawW = bmpW * scale;
                double drawH = bmpH * scale;
                double drawX = contentX + (contentW - drawW) / 2;
                double drawY = contentY + (contentH - drawH) / 2;

                // Nearest-neighbor for pixel art
                RenderOptions.SetBitmapInterpolationMode(this, Avalonia.Media.Imaging.BitmapInterpolationMode.None);
                context.DrawImage(bitmap, new Rect(drawX, drawY, drawW, drawH));

                // Draw viewport rectangle
                DrawViewportRect(context, drawX, drawY, drawW, drawH, bmpW, bmpH);
            }
        }
        else
        {
            // No map loaded — show placeholder
            var noMapText = new FormattedText("No map",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 10, new SolidColorBrush(TitleTextColor));
            context.DrawText(noMapText,
                new Point(contentX + (contentW - noMapText.Width) / 2,
                           contentY + (contentH - noMapText.Height) / 2));
        }

        // Resize grip (bottom-right corner)
        double gx = w - ResizeGripSize - 2;
        double gy = h - ResizeGripSize - 2;
        var gripPen = new Pen(new SolidColorBrush(ResizeGripColor), 1.5);
        for (int i = 0; i < 3; i++)
        {
            double offset = i * 3.5;
            context.DrawLine(gripPen,
                new Point(gx + ResizeGripSize - offset, gy + ResizeGripSize),
                new Point(gx + ResizeGripSize, gy + ResizeGripSize - offset));
        }
    }

    private void DrawViewportRect(DrawingContext context,
        double drawX, double drawY, double drawW, double drawH,
        double bmpW, double bmpH)
    {
        if (_mapCanvas?.MapDataSource == null) return;
        var (minX, minY, maxX, maxY) = _mapCanvas.MapDataSource.GetBounds();
        int tileSpanX = maxX - minX + 1;
        int tileSpanY = maxY - minY + 1;
        if (tileSpanX <= 0 || tileSpanY <= 0) return;

        double zoom = _mapCanvas.ZoomLevel;
        double canvasW = _mapCanvas.Bounds.Width;
        double canvasH = _mapCanvas.Bounds.Height;
        double viewX = _mapCanvas.ViewX;
        double viewY = _mapCanvas.ViewY;

        // Viewport in tile coordinates
        double vpTileLeft = viewX / TileSize;
        double vpTileTop = viewY / TileSize;
        double vpTileWidth = canvasW / zoom / TileSize;
        double vpTileHeight = canvasH / zoom / TileSize;

        // Convert tile coordinates to minimap-local pixel coordinates
        // Each tile is MiniMapTileSize pixels in the bitmap
        double scaleX = drawW / bmpW;
        double scaleY = drawH / bmpH;

        double rectX = drawX + (vpTileLeft - minX) * MiniMapTileSize * scaleX;
        double rectY = drawY + (vpTileTop - minY) * MiniMapTileSize * scaleY;
        double rectW = vpTileWidth * MiniMapTileSize * scaleX;
        double rectH = vpTileHeight * MiniMapTileSize * scaleY;

        // Clamp to content area
        var contentRect = new Rect(drawX, drawY, drawW, drawH);
        var vpRect = new Rect(rectX, rectY, rectW, rectH).Intersect(contentRect);
        if (vpRect.Width <= 0 || vpRect.Height <= 0)
        {
            // Viewport is fully outside — draw at edge as indicator
            vpRect = new Rect(
                Math.Clamp(rectX, drawX, drawX + drawW - 4),
                Math.Clamp(rectY, drawY, drawY + drawH - 4),
                Math.Max(4, Math.Min(rectW, drawW)),
                Math.Max(4, Math.Min(rectH, drawH)));
        }

        var pen = new Pen(new SolidColorBrush(ViewportRectColor), 1.5);
        var fill = new SolidColorBrush(Color.FromArgb(30, 0x89, 0xb4, 0xfa));
        context.DrawRectangle(fill, pen, vpRect);
    }

    // ── Pointer handling ──

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;

        var pos = point.Position;
        e.Handled = true;

        // Resize grip?
        if (pos.X > Bounds.Width - ResizeGripSize - 4 && pos.Y > Bounds.Height - ResizeGripSize - 4)
        {
            _isResizing = true;
            _interactionStart = pos;
            _startWidth = Width;
            _startHeight = Height;
            Cursor = new Cursor(StandardCursorType.BottomRightCorner);
            return;
        }

        // Title bar drag?
        if (pos.Y < TitleBarHeight)
        {
            _isDraggingOverlay = true;
            _interactionStart = pos;
            Cursor = new Cursor(StandardCursorType.Hand);
            return;
        }

        // Click on minimap area → navigate
        _isNavigating = true;
        NavigateToMinimapPosition(pos);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetCurrentPoint(this);
        var pos = point.Position;

        if (_isDraggingOverlay)
        {
            var delta = pos - _interactionStart;
            _translate.X += delta.X;
            _translate.Y += delta.Y;
            e.Handled = true;
            return;
        }

        if (_isResizing)
        {
            var delta = pos - _interactionStart;
            Width = Math.Clamp(_startWidth + delta.X, OverlayMinW, OverlayMaxW);
            Height = Math.Clamp(_startHeight + delta.Y, OverlayMinH, OverlayMaxH);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        if (_isNavigating)
        {
            NavigateToMinimapPosition(pos);
            e.Handled = true;
            return;
        }

        // Update cursor based on hover zone
        if (pos.X > Bounds.Width - ResizeGripSize - 4 && pos.Y > Bounds.Height - ResizeGripSize - 4)
            Cursor = new Cursor(StandardCursorType.BottomRightCorner);
        else if (pos.Y < TitleBarHeight)
            Cursor = new Cursor(StandardCursorType.Hand);
        else
            Cursor = new Cursor(StandardCursorType.Arrow);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isDraggingOverlay = false;
        _isResizing = false;
        _isNavigating = false;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    private void NavigateToMinimapPosition(Point pos)
    {
        if (_mapCanvas?.MapDataSource == null) return;
        var bitmap = _mapCanvas.MinimapBitmap;
        if (bitmap == null) return;

        double bmpW = bitmap.Size.Width;
        double bmpH = bitmap.Size.Height;
        if (bmpW <= 0 || bmpH <= 0) return;

        // Compute the content area and bitmap draw rect (same as in Render)
        double w = Bounds.Width;
        double h = Bounds.Height;
        double contentTop = TitleBarHeight + 2;
        double contentW = w - 8;
        double contentH = h - contentTop - 6;
        double contentX = 4;
        double contentY = contentTop;

        double scale = Math.Min(contentW / bmpW, contentH / bmpH);
        double drawW = bmpW * scale;
        double drawH = bmpH * scale;
        double drawX = contentX + (contentW - drawW) / 2;
        double drawY = contentY + (contentH - drawH) / 2;

        // Convert click position to tile coordinates
        double relX = (pos.X - drawX) / drawW;
        double relY = (pos.Y - drawY) / drawH;
        if (relX < 0 || relX > 1 || relY < 0 || relY > 1) return;

        var (minX, minY, maxX, maxY) = _mapCanvas.MapDataSource.GetBounds();
        int tileSpanX = maxX - minX + 1;
        int tileSpanY = maxY - minY + 1;

        double targetTileX = minX + relX * tileSpanX;
        double targetTileY = minY + relY * tileSpanY;

        // Center the main canvas viewport on this tile
        double zoom = _mapCanvas.ZoomLevel;
        double canvasW = _mapCanvas.Bounds.Width;
        double canvasH = _mapCanvas.Bounds.Height;
        _mapCanvas.ViewX = targetTileX * TileSize - canvasW / 2.0 / zoom;
        _mapCanvas.ViewY = targetTileY * TileSize - canvasH / 2.0 / zoom;
    }

    /// <summary>Prevent pointer events from passing through to the map canvas below.</summary>
    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        e.Handled = true;
    }
}
