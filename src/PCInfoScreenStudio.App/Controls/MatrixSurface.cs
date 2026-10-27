using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation.Peers;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.ViewModels;

namespace PCInfoScreenStudio.Controls;

public sealed class MatrixSurface : FrameworkElement
{
    private MatrixWorkspaceViewModel? _model;
    private int _x, _y;
    private int _cursorTile = -1, _cursorLayout = -1;
    public MatrixSurface()
    {
        DataContextChanged += (_, e) =>
        {
            if (_model is not null) _model.PreviewChanged -= OnPreviewChanged;
            _model = e.NewValue as MatrixWorkspaceViewModel;
            if (_model is not null) _model.PreviewChanged += OnPreviewChanged;
            _cursorTile = _cursorLayout = -1;
            OnPreviewChanged(this, EventArgs.Empty);
        };
        ToolTip = "Click a tile to select it. Ctrl+arrow keys select tiles. Pixel art: drag to paint, right-click to erase; arrows move, Space paints, Delete erases.";
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
    private void OnPreviewChanged(object? sender, EventArgs e)
    {
        if (_model is not null)
        {
            var region = _model.SelectedContentRegion;
            if (_cursorTile != _model.SelectedTile || _cursorLayout != _model.Layout)
            { _x = region.X; _y = region.Y; _cursorTile = _model.SelectedTile; _cursorLayout = _model.Layout; }
            _x = Math.Clamp(_x, region.X, region.X + region.Width - 1);
            _y = Math.Clamp(_y, region.Y, region.Y + region.Height - 1);
        }
        InvalidateVisual();
    }
    private (double Scale, double Left, double Top) Geometry()
    {
        double scale = Math.Max(.1, Math.Min(ActualWidth / 90, ActualHeight / 10));
        return (scale, (ActualWidth - scale * 90) / 2, (ActualHeight - scale * 10) / 2);
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (_model is null) return;
        var rgb = _model.PreviewPixels(); var g = Geometry(); const double brightness = 1.0;
        for (int y = 0; y < 10; y++) for (int x = 0; x < 90; x++)
        {
            int p = (y * 90 + x) * 3;
            var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb[p] * brightness), (byte)(rgb[p + 1] * brightness), (byte)(rgb[p + 2] * brightness)));
            dc.DrawRectangle(brush, null, new Rect(g.Left + x * g.Scale, g.Top + y * g.Scale, g.Scale * .84, g.Scale * .84));
        }
        for (int i = 0; i < _model.TileCount; i++)
        {
            var cell = MatrixSettings.Region(_model.Layout, i);
            dc.DrawRectangle(null, new Pen((Brush)FindResource("BorderBrush"), .8), new Rect(g.Left + cell.X * g.Scale - 1, g.Top + cell.Y * g.Scale - 1, cell.Width * g.Scale, cell.Height * g.Scale));
        }
        var region = MatrixSettings.Region(_model.Layout, _model.SelectedTile);
        dc.DrawRectangle(null, new Pen((Brush)FindResource("AccentBrush"), 1), new Rect(g.Left + region.X * g.Scale - 1, g.Top + region.Y * g.Scale - 1, region.Width * g.Scale, region.Height * g.Scale));
        if (IsKeyboardFocusWithin && _model.ContentKind == MatrixContent.PixelArt) dc.DrawRectangle(null, new Pen(Brushes.White, 1), new Rect(g.Left + _x * g.Scale, g.Top + _y * g.Scale, g.Scale, g.Scale));
    }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e); Focus();
        if (_model is null || e.ChangedButton is not (MouseButton.Left or MouseButton.Right)) return;
        int previous = _model.SelectedTile;
        if (!SelectTileAt(e.GetPosition(this))) return;
        e.Handled = true;
        // Selecting another tile must not also draw into its saved artwork.
        if (previous != _model.SelectedTile) return;
        if (_model?.ContentKind != MatrixContent.PixelArt) return;
        CaptureMouse(); Paint(e.GetPosition(this), e.ChangedButton == MouseButton.Right); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hit = HitTile(e.GetPosition(this));
        Cursor = hit >= 0 && hit == _model?.SelectedTile && _model?.ContentKind == MatrixContent.PixelArt ? Cursors.Cross : hit >= 0 ? Cursors.Hand : Cursors.Arrow;
        if (IsMouseCaptured && (e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed)) Paint(e.GetPosition(this), e.RightButton == MouseButtonState.Pressed);
    }
    protected override void OnMouseUp(MouseButtonEventArgs e) { base.OnMouseUp(e); ReleaseMouseCapture(); }
    private void Paint(Point point, bool erase)
    {
        var g = Geometry(); int x = (int)Math.Floor((point.X - g.Left) / g.Scale), y = (int)Math.Floor((point.Y - g.Top) / g.Scale);
        if (x is < 0 or >= 90 || y is < 0 or >= 10) return;
        _x = x; _y = y; _model?.PaintPixel(x, y, erase); InvalidateVisual();
    }
    private int HitTile(Point point)
    {
        if (_model is null) return -1;
        var g = Geometry();
        return MatrixSettings.TileAt(_model.Layout, (int)Math.Floor((point.X - g.Left) / g.Scale), (int)Math.Floor((point.Y - g.Top) / g.Scale));
    }
    public bool SelectTileAt(Point point)
    {
        int hit = HitTile(point); if (hit < 0 || _model is null) return false;
        _model.SelectedTile = hit; return true;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_model is not null && (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || e.Key is Key.Home or Key.End))
        {
            int current = _model.SelectedTile, count = _model.TileCount;
            int next = e.Key == Key.Home ? 0 : e.Key == Key.End ? count - 1 : current;
            if (e.Key == Key.Left) next -= _model.Layout == 9 ? 2 : 1;
            if (e.Key == Key.Right) next += _model.Layout == 9 ? 2 : 1;
            if (_model.Layout == 9 && e.Key == Key.Up && current % 2 == 1) next--;
            if (_model.Layout == 9 && e.Key == Key.Down && current % 2 == 0) next++;
            if (next >= 0 && next < count) _model.SelectedTile = next;
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End) { e.Handled = true; return; }
        }
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Space or Key.Delete)) return;
        var r = _model is not null ? _model.SelectedContentRegion : (X:0,Y:0,Width:90,Height:10);
        _x = Math.Clamp(_x + (e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0), r.X, r.X + r.Width - 1);
        _y = Math.Clamp(_y + (e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0), r.Y, r.Y + r.Height - 1);
        if (e.Key is Key.Space or Key.Delete) _model?.PaintPixel(_x, _y, e.Key == Key.Delete);
        e.Handled = true; InvalidateVisual();
    }
}
