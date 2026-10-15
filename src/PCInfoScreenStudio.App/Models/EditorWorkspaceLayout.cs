namespace PCInfoScreenStudio.Models;

// Application preferences only. These never enter a theme or its Undo history.
public sealed class EditorWorkspaceLayout
{
    public DockLayout Left { get; set; } = new() { Width = 300 };
    public DockLayout Right { get; set; } = new() { Width = 330 };

    public EditorWorkspaceLayout ValidatedCopy()
    {
        var copy = new EditorWorkspaceLayout { Left = DockLayout.Copy(Left, 300), Right = DockLayout.Copy(Right, 330) };
        var leftIds = copy.Left.Panels.Select(p => p.Id).ToHashSet();
        copy.Right.Panels.RemoveAll(p => leftIds.Contains(p.Id));
        return copy;
    }
}

public sealed class DockLayout
{
    public double Width { get; set; } = 300;
    public List<WorkspacePanelLayout> Panels { get; set; } = [];

    internal static DockLayout Copy(DockLayout? source, double defaultWidth) => new()
    {
        Width = source is not null && double.IsFinite(source.Width) ? Math.Clamp(source.Width, 240, 460) : defaultWidth,
        Panels = (source?.Panels ?? []).Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Id) && p.Id.Length <= 40)
            .DistinctBy(p => p.Id).Take(20).Select(p => new WorkspacePanelLayout
            {
                Id = p.Id, IsOpen = p.IsOpen, IsFloating = p.IsFloating,
                FloatingBounds = FloatingPanelBounds.Copy(p.FloatingBounds),
                Weight = double.IsFinite(p.Weight) ? Math.Clamp(p.Weight, .1, 10) : 1,
                PreferredHeight = p.PreferredHeight is double height && double.IsFinite(height) && height > 0
                    ? Math.Clamp(height, 72, 10000) : null
            }).ToList()
    };
}

public sealed class WorkspacePanelLayout
{
    public string Id { get; set; } = string.Empty;
    public bool IsOpen { get; set; } = true;
    public bool IsFloating { get; set; }
    public FloatingPanelBounds? FloatingBounds { get; set; }
    public double Weight { get; set; } = 1;
    public double? PreferredHeight { get; set; }
}

// Screen position is stored in device pixels; size in WPF logical units.
public sealed class FloatingPanelBounds
{
    public double ScreenX { get; set; }
    public double ScreenY { get; set; }
    public double Width { get; set; } = 340;
    public double Height { get; set; } = 400;

    internal static FloatingPanelBounds? Copy(FloatingPanelBounds? bounds) => bounds is not null &&
        double.IsFinite(bounds.ScreenX) && double.IsFinite(bounds.ScreenY) &&
        Math.Abs(bounds.ScreenX) <= 100000 && Math.Abs(bounds.ScreenY) <= 100000 &&
        double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height) && bounds.Width > 0 && bounds.Height > 0
        ? new() { ScreenX = bounds.ScreenX, ScreenY = bounds.ScreenY,
            Width = Math.Clamp(bounds.Width, 260, 1200), Height = Math.Clamp(bounds.Height, 120, 1600) } : null;
}
