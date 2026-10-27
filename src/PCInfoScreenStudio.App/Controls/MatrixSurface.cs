using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.ViewModels;

namespace PCInfoScreenStudio.Controls;

public sealed class MatrixSurface : FrameworkElement
{
    private MatrixWorkspaceViewModel? _model;
    private int _x, _y;
    public MatrixSurface()
    {
        DataContextChanged += (_, e) =>
        {
            if (_model is not null) _model.PreviewChanged -= OnPreviewChanged;
            _model = e.NewValue as MatrixWorkspaceViewModel;
            if (_model is not null) _model.PreviewChanged += OnPreviewChanged;
            InvalidateVisual();
        };
        ToolTip = "Pixel art: drag to paint, right-click to erase. Arrow keys move; Space paints; Delete erases.";
    }
    private void OnPreviewChanged(object? sender, EventArgs e)
    {
        if (_model is not null)
        {
            var region = MatrixSettings.Region(_model.Layout, _model.SelectedTile);
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
        var region = MatrixSettings.Region(_model.Layout, _model.SelectedTile);
        dc.DrawRectangle(null, new Pen((Brush)FindResource("AccentBrush"), 1), new Rect(g.Left + region.X * g.Scale - 1, g.Top + region.Y * g.Scale - 1, region.Width * g.Scale, region.Height * g.Scale));
        if (IsKeyboardFocusWithin) dc.DrawRectangle(null, new Pen(Brushes.White, 1), new Rect(g.Left + _x * g.Scale, g.Top + _y * g.Scale, g.Scale, g.Scale));
    }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e); Focus();
        if (_model?.ContentKind != MatrixContent.PixelArt) return;
        CaptureMouse(); Paint(e.GetPosition(this), e.ChangedButton == MouseButton.Right); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    { base.OnMouseMove(e); if (IsMouseCaptured && (e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed)) Paint(e.GetPosition(this), e.RightButton == MouseButtonState.Pressed); }
    protected override void OnMouseUp(MouseButtonEventArgs e) { base.OnMouseUp(e); ReleaseMouseCapture(); }
    private void Paint(Point point, bool erase)
    {
        var g = Geometry(); int x = (int)Math.Floor((point.X - g.Left) / g.Scale), y = (int)Math.Floor((point.Y - g.Top) / g.Scale);
        if (x is < 0 or >= 90 || y is < 0 or >= 10) return;
        _x = x; _y = y; _model?.PaintPixel(x, y, erase); InvalidateVisual();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Space or Key.Delete)) return;
        var r = _model is not null ? MatrixSettings.Region(_model.Layout, _model.SelectedTile) : (X:0,Y:0,Width:90,Height:10);
        _x = Math.Clamp(_x + (e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0), r.X, r.X + r.Width - 1);
        _y = Math.Clamp(_y + (e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0), r.Y, r.Y + r.Height - 1);
        if (e.Key is Key.Space or Key.Delete) _model?.PaintPixel(_x, _y, e.Key == Key.Delete);
        e.Handled = true; InvalidateVisual();
    }
}
