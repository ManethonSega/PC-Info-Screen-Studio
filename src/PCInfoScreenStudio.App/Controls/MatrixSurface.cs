using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation.Peers;
using PCInfoScreenStudio.ViewModels;

namespace PCInfoScreenStudio.Controls;

public sealed class MatrixSurface : FrameworkElement
{
    private MatrixWorkspaceViewModel? _model;
    private Point _dragStart;
    private Guid? _dragId;
    private int _startX, _startY, _cursorX, _cursorY;
    public MatrixSurface()
    {
        DataContextChanged += (_, e) =>
        {
            if (_model is not null) _model.PreviewChanged -= OnPreviewChanged;
            _model = e.NewValue as MatrixWorkspaceViewModel;
            if (_model is not null) _model.PreviewChanged += OnPreviewChanged;
            InvalidateVisual();
        };
        ToolTip = "Select and drag widgets. Moves snap to one LED. Arrow keys nudge; Shift+arrows move ten LEDs. Escape cancels a drag. Pixel art has a separate Paint pixels tool.";
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
    private void OnPreviewChanged(object? sender, EventArgs e) => InvalidateVisual();
    private (double Scale, double Left, double Top) Geometry()
    {
        double scale = Math.Max(.1, Math.Min(ActualWidth / 90, ActualHeight / 10));
        return (scale, (ActualWidth - scale * 90) / 2, (ActualHeight - scale * 10) / 2);
    }
    private Point GridPoint(Point point)
    { var g = Geometry(); return new((point.X - g.Left) / g.Scale, (point.Y - g.Top) / g.Scale); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (_model is null) return;
        var rgb = _model.PreviewPixels(); var g = Geometry();
        for (int y = 0; y < 10; y++) for (int x = 0; x < 90; x++)
        {
            int p = (y * 90 + x) * 3;
            Brush brush = rgb[p] == 0 && rgb[p + 1] == 0 && rgb[p + 2] == 0 ? (Brush)FindResource("BorderBrush") : new SolidColorBrush(Color.FromRgb(rgb[p], rgb[p + 1], rgb[p + 2]));
            dc.DrawRectangle(brush, null, new Rect(g.Left + x * g.Scale, g.Top + y * g.Scale, g.Scale * .84, g.Scale * .84));
        }
        if (_model.SelectedWidget is not { } w) return;
        dc.DrawRectangle(null, new Pen((Brush)FindResource("AccentBrush"), 1), new Rect(g.Left + w.X * g.Scale - 1, g.Top + w.Y * g.Scale - 1, w.Width * g.Scale, w.Height * g.Scale));
        if (_model.IsPainting && IsKeyboardFocusWithin)
            dc.DrawRectangle(null, new Pen(Brushes.White, 1), new Rect(g.Left + (w.X + Math.Clamp(_cursorX, 0, w.Width - 1)) * g.Scale, g.Top + (w.Y + Math.Clamp(_cursorY, 0, w.Height - 1)) * g.Scale, g.Scale, g.Scale));
    }
    public bool SelectWidgetAt(Point point)
    {
        if (_model is null) return false;
        var p = GridPoint(point); var widget = _model.HitWidget((int)Math.Floor(p.X), (int)Math.Floor(p.Y));
        if (widget is null) return false;
        _model.SelectedWidgetId = widget.Id; return true;
    }
    public bool BeginDrag(Point point)
    {
        if (_model is null || !SelectWidgetAt(point) || !_model.IsEditable) return false;
        _dragId = _model.SelectedWidgetId; _dragStart = GridPoint(point); _startX = _model.X; _startY = _model.Y; return true;
    }
    public void DragTo(Point point)
    {
        if (_model is null || _dragId != _model.SelectedWidgetId) return;
        var p = GridPoint(point);
        _model.MoveSelectedTo(_startX + (int)Math.Round(p.X - _dragStart.X, MidpointRounding.AwayFromZero), _startY + (int)Math.Round(p.Y - _dragStart.Y, MidpointRounding.AwayFromZero));
    }
    public void EndDrag(bool cancel = false)
    {
        if (cancel && _dragId == _model?.SelectedWidgetId) _model?.MoveSelectedTo(_startX, _startY);
        _dragId = null; if (IsMouseCaptured) ReleaseMouseCapture();
    }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e); Focus(); if (_model is null) return;
        if (_model.IsPainting && _model.IsEditable && e.ChangedButton is MouseButton.Left or MouseButton.Right)
        { CaptureMouse(); Paint(e.GetPosition(this), e.ChangedButton == MouseButton.Right); e.Handled = true; return; }
        if (e.ChangedButton == MouseButton.Left && BeginDrag(e.GetPosition(this))) { CaptureMouse(); e.Handled = true; }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_model is null) return;
        Cursor = _model.IsPainting ? Cursors.Cross : Cursors.Arrow;
        if (!IsMouseCaptured) return;
        if (_model.IsPainting) Paint(e.GetPosition(this), e.RightButton == MouseButtonState.Pressed);
        else if (e.LeftButton == MouseButtonState.Pressed) DragTo(e.GetPosition(this));
    }
    protected override void OnMouseUp(MouseButtonEventArgs e) { base.OnMouseUp(e); EndDrag(); }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); EndDrag(true); }
    private void Paint(Point point, bool erase)
    {
        if (_model?.SelectedWidget is not { } w) return;
        var p = GridPoint(point); int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
        if (x < w.X || x >= w.X + w.Width || y < w.Y || y >= w.Y + w.Height) return;
        _cursorX = x - w.X; _cursorY = y - w.Y; _model.PaintPixel(x, y, erase);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e); if (_model is null) return;
        if (e.Key == Key.Escape) { EndDrag(true); e.Handled = true; return; }
        if (e.Key == Key.D && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { if (_model.DuplicateCommand.CanExecute(null)) _model.DuplicateCommand.Execute(null); e.Handled = true; return; }
        if (e.Key == Key.Delete && !_model.IsPainting) { if (_model.DeleteCommand.CanExecute(null)) _model.DeleteCommand.Execute(null); e.Handled = true; return; }
        if (!_model.IsEditable) return;
        int dx = e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0, dy = e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0;
        if (_model.IsPainting)
        {
            _cursorX = Math.Clamp(_cursorX + dx, 0, _model.Width - 1); _cursorY = Math.Clamp(_cursorY + dy, 0, _model.WidgetHeight - 1);
            if (e.Key is Key.Space or Key.Delete) _model.PaintPixel(_model.X + _cursorX, _model.Y + _cursorY, e.Key == Key.Delete);
        }
        else { int amount = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1; _model.MoveSelectedTo(_model.X + dx * amount, _model.Y + dy * amount); }
        if (dx != 0 || dy != 0 || e.Key is Key.Space or Key.Delete) { e.Handled = true; InvalidateVisual(); }
    }
}
