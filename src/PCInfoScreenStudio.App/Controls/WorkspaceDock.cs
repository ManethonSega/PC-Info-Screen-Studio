using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Controls;

/// <summary>A persistent dock of independent panels. Content stays alive when collapsed.</summary>
[ContentProperty(nameof(Panels))]
public sealed class WorkspaceDock : Grid
{
    public const double RailWidth = 104;
    public const double DividerWidth = 6;
    private const double MinimumPanelHeight = 72;
    private readonly StackPanel _rail = new();
    private readonly Grid _stack = new() { VerticalAlignment = VerticalAlignment.Top };
    private readonly ScrollViewer _stackScroll;
    private readonly ColumnDefinition _contentColumn = new();
    private readonly ColumnDefinition _dividerColumn = new();
    private readonly WorkspaceWidthGrip _widthGrip;
    private readonly Dictionary<WorkspacePanel, Border> _hosts = [];
    private readonly Dictionary<WorkspacePanel, ToggleButton> _tabs = [];
    private readonly List<WorkspacePanel> _order = [];
    private bool _ready;
    private bool _restoring;
    private double _maximumPanelWidth = 460;
    private double _preferredPanelWidth = 300;
    private double _widthBeforeDrag;

    public ObservableCollection<WorkspacePanel> Panels { get; } = [];
    public bool RailOnRight { get; set; }
    public bool HasOpenPanels => _order.Any(p => p.Visibility == Visibility.Visible && p.IsOpen);
    public double PreferredPanelWidth => _preferredPanelWidth;
    public double EffectivePanelWidth => HasOpenPanels ? _contentColumn.Width.Value : 0;
    public event EventHandler? LayoutChanged;
    public event EventHandler? LayoutSizeChanged;

    public WorkspaceDock()
    {
        ClipToBounds = true;
        SetResourceReference(BackgroundProperty, "PanelBrush");
        _stackScroll = new ScrollViewer { Content = _stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false,
            VerticalContentAlignment = VerticalAlignment.Top };
        _widthGrip = new WorkspaceWidthGrip(this) { Width = DividerWidth, HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch, Cursor = Cursors.SizeWE, Focusable = true };
        _widthGrip.SetResourceReference(StyleProperty, "WorkspaceWidthGripStyle");
        _widthGrip.DragStarted += (_, _) => _widthBeforeDrag = _preferredPanelWidth;
        _widthGrip.DragDelta += (_, e) => ResizePanel(EffectivePanelWidth + (RailOnRight ? -e.HorizontalChange : e.HorizontalChange), false);
        _widthGrip.DragCompleted += (_, e) =>
        {
            if (e.Canceled) ResizePanel(_widthBeforeDrag, false);
            else LayoutChanged?.Invoke(this, EventArgs.Empty);
        };
        Loaded += (_, _) => EnsureReady();
        SizeChanged += (_, _) => UpdateStackLimits();
    }

    public void EnsureReady()
    {
        if (_ready) return;
        ColumnDefinitions.Add(RailOnRight ? _dividerColumn : new ColumnDefinition { Width = new GridLength(RailWidth) });
        ColumnDefinitions.Add(_contentColumn);
        ColumnDefinitions.Add(RailOnRight ? new ColumnDefinition { Width = new GridLength(RailWidth) } : _dividerColumn);
        var railScroll = new ScrollViewer { Content = _rail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        SetColumn(railScroll, RailOnRight ? 2 : 0); Children.Add(railScroll);
        SetColumn(_stackScroll, 1); Children.Add(_stackScroll);
        SetColumn(_widthGrip, RailOnRight ? 0 : 2); Children.Add(_widthGrip);
        AutomationProperties.SetName(_widthGrip, RailOnRight ? "Right panel width" : "Left panel width");
        foreach (var panel in Panels)
        {
            // Collapsed panels remain bound even while their hosts are outside the visual tree.
            panel.SetBinding(DataContextProperty, new Binding(nameof(DataContext)) { Source = this });
            _order.Add(panel);
            panel.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            panel.VerticalContentAlignment = VerticalAlignment.Stretch;
            panel.StateChanged += OnPanelStateChanged;
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = new TextBlock { Text = panel.Icon, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14,
                Width = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            label.Children.Add(icon);
            label.Children.Add(new TextBlock { Text = panel.Title, VerticalAlignment = VerticalAlignment.Center });
            var tab = new ToggleButton { Content = label, Margin = new Thickness(4, 3, 4, 0),
                ToolTip = "Open or collapse " + panel.Title.ToLowerInvariant() + ". Other open panels stay visible." };
            tab.SetResourceReference(StyleProperty, "WorkspaceTabStyle");
            tab.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(WorkspacePanel.IsOpen)) { Source = panel, Mode = BindingMode.TwoWay });
            AutomationProperties.SetName(tab, panel.Title + " panel");
            _tabs.Add(panel, tab);
            _hosts.Add(panel, CreateHost(panel));
        }
        _ready = true;
        RebuildStack();
    }

    private Border CreateHost(WorkspacePanel panel)
    {
        var host = new Grid();
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new DockPanel { Margin = new Thickness(10, 5, 5, 5), LastChildFill = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(actions, Dock.Right); header.Children.Add(actions);
        AddHeaderAction(actions, panel, "\uE70E", "Move " + panel.Title.ToLowerInvariant() + " panel up", () => MovePanel(panel, -1));
        AddHeaderAction(actions, panel, "\uE70D", "Move " + panel.Title.ToLowerInvariant() + " panel down", () => MovePanel(panel, 1));
        AddHeaderAction(actions, panel, "\uE711", "Collapse " + panel.Title.ToLowerInvariant() + " panel", () => { panel.IsOpen = false; _tabs[panel].Focus(); });
        header.Children.Add(new TextBlock { Text = panel.Title, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        host.Children.Add(header);
        FrameworkElement body = panel;
        if (panel.IsScrollable)
            body = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false };
        body.Margin = new Thickness(10, 2, 10, 10);
        SetRow(body, 1); host.Children.Add(body);
        var border = new Border { Child = host, BorderThickness = new Thickness(1, 0, 1, 1) };
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return border;
    }

    private void AddHeaderAction(Panel actions, WorkspacePanel panel, string glyph, string name, Action action)
    {
        var button = new Button { Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10 },
            Width = 22, Height = 24, Padding = new Thickness(3), Margin = new Thickness(0), ToolTip = name };
        button.SetResourceReference(StyleProperty, "QuietButtonStyle");
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => action();
        actions.Children.Add(button);
    }

    private void OnPanelStateChanged(object? sender, EventArgs e)
    {
        if (!_ready || _restoring) return;
        RebuildStack();
        LayoutSizeChanged?.Invoke(this, EventArgs.Empty);
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildStack()
    {
        _stack.Children.Clear(); _stack.RowDefinitions.Clear();
        var open = _order.Where(p => p.Visibility == Visibility.Visible && p.IsOpen).ToArray();
        var tabs = _order.Select(p => _tabs[p]).ToArray();
        if (!_rail.Children.OfType<ToggleButton>().SequenceEqual(tabs))
        {
            _rail.Children.Clear();
            foreach (var tab in tabs) _rail.Children.Add(tab);
        }
        foreach (var panel in _order) _tabs[panel].Visibility = panel.Visibility;
        for (var i = 0; i < open.Length; i++)
        {
            var panel = open[i];
            if (i > 0)
            {
                _stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(DividerWidth) });
                var splitter = new GridSplitter { Height = DividerWidth, HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch, ResizeDirection = GridResizeDirection.Rows,
                    ResizeBehavior = GridResizeBehavior.PreviousAndNext, ShowsPreview = false, Focusable = true };
                splitter.SetResourceReference(StyleProperty, "WorkspaceStackSplitterStyle");
                AutomationProperties.SetName(splitter, "Resize " + open[i - 1].Title.ToLowerInvariant() + " and " + panel.Title.ToLowerInvariant() + " panels");
                var preceding = open[i - 1];
                double? beforeDrag = null;
                splitter.DragStarted += (_, _) =>
                {
                    beforeDrag = preceding.PreferredHeight;
                    PrepareHeightResize(preceding);
                };
                splitter.DragCompleted += (_, e) =>
                {
                    if (!e.Canceled) SaveHeights();
                    else { preceding.PreferredHeight = beforeDrag; RebuildStack(); }
                };
                splitter.PreviewKeyDown += (_, e) =>
                {
                    if (e.Key is Key.Up or Key.Down) PrepareHeightResize(preceding);
                };
                splitter.PreviewKeyUp += (_, e) =>
                {
                    if (e.Key is Key.Up or Key.Down)
                        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(SaveHeights));
                };
                SetRow(splitter, _stack.RowDefinitions.Count - 1); _stack.Children.Add(splitter);
            }
            _stack.RowDefinitions.Add(new RowDefinition
            {
                Height = panel.PreferredHeight is double height ? new GridLength(height) : GridLength.Auto,
                MinHeight = MinimumPanelHeight
            });
            var host = _hosts[panel]; SetRow(host, _stack.RowDefinitions.Count - 1); _stack.Children.Add(host);
        }
        UpdateWidths(); UpdateStackLimits();
    }

    private void PrepareHeightResize(WorkspacePanel panel)
    {
        var host = _hosts[panel];
        var row = _stack.RowDefinitions[GetRow(host)];
        panel.PreferredHeight ??= host.ActualHeight;
        if (row.Height.IsAuto) row.Height = new GridLength(host.ActualHeight);
        UpdateStackLimits();
    }

    private void SaveHeights()
    {
        _stack.UpdateLayout();
        foreach (var panel in _order.Where(p => p.Visibility == Visibility.Visible && p.IsOpen))
        {
            var row = _stack.RowDefinitions[GetRow(_hosts[panel])];
            if (row.Height.IsAbsolute) panel.PreferredHeight = Math.Clamp(row.Height.Value, MinimumPanelHeight, 10000);
        }
        UpdateStackLimits();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateStackLimits()
    {
        if (!_ready) return;
        var open = _order.Where(p => p.Visibility == Visibility.Visible && p.IsOpen).ToArray();
        var available = Math.Max(MinimumPanelHeight, ActualHeight);
        var automaticLimit = Math.Max(MinimumPanelHeight,
            (available - Math.Max(0, open.Length - 1) * DividerWidth) / Math.Max(1, open.Length));
        foreach (var panel in open)
        {
            var host = _hosts[panel];
            // Auto rows follow live content; only an explicit resize creates a saved height.
            host.MaxHeight = panel.PreferredHeight.HasValue ? available : automaticLimit;
            _stack.RowDefinitions[GetRow(host)].MaxHeight = available;
        }
    }

    public void ConstrainPanelWidth(double maximum)
    {
        _maximumPanelWidth = Math.Clamp(maximum, 120, 460);
        if (_ready) UpdateWidths();
    }

    private void UpdateWidths()
    {
        var width = Math.Min(_preferredPanelWidth, _maximumPanelWidth);
        _contentColumn.Width = new GridLength(HasOpenPanels ? width : 0);
        _dividerColumn.Width = new GridLength(HasOpenPanels ? DividerWidth : 0);
        _stackScroll.Visibility = _widthGrip.Visibility = HasOpenPanels ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void ResizePanel(double width, bool save)
    {
        if (!double.IsFinite(width)) return;
        _preferredPanelWidth = Math.Clamp(width, 240, 460);
        LayoutSizeChanged?.Invoke(this, EventArgs.Empty);
        UpdateWidths();
        if (save) LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MovePanel(WorkspacePanel panel, int direction)
    {
        EnsureReady();
        var visible = _order.Where(p => p.Visibility == Visibility.Visible).ToList();
        var index = visible.IndexOf(panel); var target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= visible.Count) return;
        var fromIndex = _order.IndexOf(panel); var toIndex = _order.IndexOf(visible[target]);
        (_order[fromIndex], _order[toIndex]) = (_order[toIndex], _order[fromIndex]);
        RebuildStack(); LayoutChanged?.Invoke(this, EventArgs.Empty);
        _tabs[panel].Focus();
    }

    public DockLayout CaptureLayout()
    {
        EnsureReady();
        return new DockLayout { Width = _preferredPanelWidth,
            Panels = _order.Select(p => new WorkspacePanelLayout
                { Id = p.PanelId, IsOpen = p.IsOpen, Weight = p.Weight, PreferredHeight = p.PreferredHeight }).ToList() };
    }

    public void RestoreLayout(DockLayout layout)
    {
        EnsureReady(); _restoring = true;
        try
        {
            _preferredPanelWidth = layout.Width;
            var known = Panels.ToDictionary(p => p.PanelId);
            _order.Clear();
            foreach (var state in layout.Panels)
                if (known.Remove(state.Id, out var panel))
                { panel.IsOpen = state.IsOpen; panel.Weight = state.Weight; panel.PreferredHeight = state.PreferredHeight; _order.Add(panel); }
            _order.AddRange(Panels.Where(p => known.ContainsKey(p.PanelId)));
            RebuildStack();
        }
        finally { _restoring = false; }
        LayoutSizeChanged?.Invoke(this, EventArgs.Empty);
    }
}
