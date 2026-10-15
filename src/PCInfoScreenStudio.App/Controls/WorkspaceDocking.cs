using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Controls;

/// <summary>Coordinates live panel ownership without recreating editor content.</summary>
internal sealed class WorkspaceDocking : IDisposable
{
    private readonly Window _owner;
    private readonly WorkspaceDock[] _docks;
    private readonly Action<Window>? _configureWindow;
    private readonly Dictionary<WorkspacePanel, bool> _home;
    private readonly Dictionary<WorkspacePanel, FloatingWorkspaceWindow> _windows = [];
    private readonly TextBlock _dragLabel = new() { Margin = new Thickness(10, 7, 10, 7) };
    private readonly Popup _dragPreview = new() { Placement = PlacementMode.Relative, StaysOpen = true,
        AllowsTransparency = true, IsHitTestVisible = false, Focusable = false };
    private UIElement? _dragSource;
    private WorkspacePanel? _dragPanel;
    private Point _pressPoint;
    private bool _dragging;
    private bool _syncing;
    private bool _disposed;
    internal bool IsDragging => _dragging;
    internal IReadOnlyCollection<FloatingWorkspaceWindow> FloatingWindows => _windows.Values;

    public WorkspaceDocking(Window owner, WorkspaceDock left, WorkspaceDock right, Action<Window>? configureWindow = null)
    {
        _owner = owner; _docks = [left, right]; _configureWindow = configureWindow;
        _dragPreview.PlacementTarget = owner;
        var dragBorder = new Border { Child = _dragLabel, BorderThickness = new Thickness(1) };
        dragBorder.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        dragBorder.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        _dragLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        _dragPreview.Child = dragBorder;
        _home = left.Panels.ToDictionary(p => p, _ => false);
        foreach (var panel in right.Panels) _home.Add(panel, true);
        foreach (var dock in _docks)
        {
            dock.Docking = this; dock.EnsureReady();
            dock.IsVisibleChanged += OnVisibilityChanged;
        }
        owner.Loaded += OnOwnerChanged;
        owner.IsVisibleChanged += OnVisibilityChanged;
        owner.StateChanged += OnOwnerChanged;
        owner.Closed += OnOwnerClosed;
    }

    private void OnOwnerChanged(object? sender, EventArgs e) => SyncWindows();
    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => SyncWindows();
    private void OnOwnerClosed(object? sender, EventArgs e) => Dispose();
    private WorkspaceDock DockFor(WorkspacePanel panel) => _docks.Single(d => d.Panels.Contains(panel));

    public EditorWorkspaceLayout DefaultLayout() => new()
    {
        Left = new() { Width = 300, Panels = _home.Where(p => !p.Value).Select(p => new WorkspacePanelLayout
            { Id = p.Key.PanelId, IsOpen = p.Key.PanelId is "widgets" or "photos" or "layers" }).ToList() },
        Right = new() { Width = 330, Panels = _home.Where(p => p.Value).Select(p => new WorkspacePanelLayout
            { Id = p.Key.PanelId, IsOpen = p.Key.PanelId == "properties" }).ToList() }
    };

    public void Restore(EditorWorkspaceLayout layout)
    {
        CancelDrag();
        layout = layout.ValidatedCopy();
        foreach (var panel in _home.Keys)
        {
            var side = layout.Left.Panels.Any(p => p.Id == panel.PanelId) ? false :
                layout.Right.Panels.Any(p => p.Id == panel.PanelId) ? true : _home[panel];
            DockFor(panel).TransferTo(panel, _docks[side ? 1 : 0]);
        }
        _docks[0].RestoreLayout(layout.Left); _docks[1].RestoreLayout(layout.Right);
        SyncWindows();
    }

    public void Dock(WorkspacePanel panel, bool right, int? index = null)
    {
        var source = DockFor(panel); var target = _docks[right ? 1 : 0];
        CloseFloatingWindow(panel);
        source.TransferTo(panel, target);
        panel.IsFloating = false; panel.IsOpen = true;
        if (index.HasValue) target.Place(panel, index.Value);
        source.Refresh(); target.Refresh(); target.NotifyChanged();
        target.Host(panel).MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    public void Float(WorkspacePanel panel, Point? screenPoint = null)
    {
        var dock = DockFor(panel);
        if (screenPoint.HasValue) CloseFloatingWindow(panel);
        panel.IsFloating = true; panel.IsOpen = true;
        dock.Refresh();
        if (_windows.TryGetValue(panel, out var window))
        {
            if (screenPoint.HasValue) window.PlaceOnScreen(screenPoint.Value);
            window.Activate();
        }
        dock.NotifyChanged();
    }

    internal void CloseFloatingWindow(WorkspacePanel panel)
    {
        if (_windows.Remove(panel, out var window)) window.DetachAndClose();
    }

    internal void SyncWindows()
    {
        if (_disposed || _syncing) return;
        _syncing = true;
        try
        {
            foreach (var panel in _home.Keys)
            {
                var dock = DockFor(panel);
                var show = panel.IsFloating && panel.IsOpen && panel.Visibility == Visibility.Visible &&
                    dock.IsVisible && _owner.IsVisible && _owner.WindowState != WindowState.Minimized;
                if (show)
                {
                    if (!_windows.TryGetValue(panel, out var window))
                    {
                        var host = dock.Host(panel); host.MaxHeight = double.PositiveInfinity;
                        window = new FloatingWorkspaceWindow(_owner, panel, host, () => dock.NotifyChanged(), () => dock.GetMinimumHeight(panel));
                        _configureWindow?.Invoke(window);
                        _windows.Add(panel, window);
                    }
                    if (!window.IsVisible) window.ShowRecovered();
                }
                else if (_windows.TryGetValue(panel, out var window)) { window.SaveBounds(); window.Hide(); }
            }
            if (!_owner.IsVisible || _owner.WindowState == WindowState.Minimized) CancelDrag();
        }
        finally { _syncing = false; }
    }

    // Header drag uses native mouse capture across owned HWNDs. No media/file drop data is intercepted.
    internal void AttachDrag(FrameworkElement source, WorkspacePanel panel)
    {
        source.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (_disposed || !_owner.IsEnabled || IsAction(e.OriginalSource as DependencyObject, source)) return;
            CancelDrag();
            _dragSource = source; _dragPanel = panel;
            _pressPoint = source.PointToScreen(e.GetPosition(source));
            if (source is not ToggleButton) source.Focus();
            source.CaptureMouse();
        };
        source.PreviewMouseMove += (_, e) =>
        {
            if (_dragSource != source || _dragPanel != panel || e.LeftButton != MouseButtonState.Pressed) return;
            var point = source.PointToScreen(e.GetPosition(source));
            var scale = VisualTreeHelper.GetDpi(source);
            if (!_dragging && Math.Abs(point.X - _pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance * scale.DpiScaleX &&
                Math.Abs(point.Y - _pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance * scale.DpiScaleY) return;
            UpdateDrag(point); e.Handled = true;
        };
        source.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_dragSource != source) return;
            var dragged = _dragging;
            if (!dragged)
            {
                // Let ToggleButton release its own capture and complete a normal rail click.
                CancelDrag(source is not ToggleButton);
                return;
            }
            CompleteDrag(source.PointToScreen(e.GetPosition(source)), false);
            if (dragged) e.Handled = true;
        };
        source.LostMouseCapture += (_, _) => { if (_dragSource == source) CancelDrag(); };
        source.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _dragSource == source) { CancelDrag(); e.Handled = true; }
        };
        // A dock header itself can receive Escape while its child controls remain in normal tab order.
        if (source is not ToggleButton) source.Focusable = true;
    }

    private static bool IsAction(DependencyObject? current, DependencyObject source)
    {
        while (current is not null && current != source)
        {
            if (current is ButtonBase) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    internal void BeginDrag(WorkspacePanel panel, UIElement source, Point point)
    { CancelDrag(); _dragPanel = panel; _dragSource = source; _pressPoint = point; }

    internal void UpdateDrag(Point screenPoint)
    {
        if (_dragPanel is null) return;
        _dragging = true;
        foreach (var dock in _docks)
        {
            if (!dock.IsVisible) continue;
            var local = dock.PointFromScreen(screenPoint);
            var inside = new Rect(dock.RenderSize).Contains(local);
            dock.ShowDockPreview(true, inside ? dock.DropIndex(local) : null);
        }
        var ownerPoint = _owner.PointFromScreen(screenPoint);
        _dragPreview.HorizontalOffset = ownerPoint.X + 14; _dragPreview.VerticalOffset = ownerPoint.Y + 18;
        _dragLabel.Text = _dragPanel.Title + "  ·  Release to place  ·  Esc to cancel";
        _dragPreview.IsOpen = true;
        if (_dragSource is FrameworkElement source)
        {
            source.Cursor = Cursors.SizeAll;
            source.Focus();
        }
    }

    internal void CompleteDrag(Point screenPoint, bool cancel)
    {
        var panel = _dragPanel; var dragged = _dragging;
        var target = !cancel && dragged ? _docks.FirstOrDefault(d => d.IsVisible && new Rect(d.RenderSize).Contains(d.PointFromScreen(screenPoint))) : null;
        var index = target?.DropIndex(target.PointFromScreen(screenPoint));
        CancelDrag();
        if (cancel || !dragged || panel is null) return;
        if (target is not null) Dock(panel, target.RailOnRight, index);
        else Float(panel, screenPoint);
    }

    internal void CancelDrag(bool releaseCapture = true)
    {
        _dragPreview.IsOpen = false;
        var source = _dragSource; _dragSource = null; _dragPanel = null; _dragging = false;
        if (source is FrameworkElement element) element.ClearValue(FrameworkElement.CursorProperty);
        if (releaseCapture && source?.IsMouseCaptured == true) source.ReleaseMouseCapture();
        foreach (var dock in _docks) dock.ShowDockPreview(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; CancelDrag();
        foreach (var panel in _windows.Keys.ToArray()) CloseFloatingWindow(panel);
        foreach (var dock in _docks) dock.IsVisibleChanged -= OnVisibilityChanged;
        _owner.Loaded -= OnOwnerChanged; _owner.StateChanged -= OnOwnerChanged;
        _owner.IsVisibleChanged -= OnVisibilityChanged; _owner.Closed -= OnOwnerClosed;
    }
}
