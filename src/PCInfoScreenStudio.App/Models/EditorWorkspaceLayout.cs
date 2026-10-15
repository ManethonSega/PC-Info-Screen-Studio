namespace PCInfoScreenStudio.Models;

// Application preferences only. These never enter a theme or its Undo history.
public sealed class EditorWorkspaceLayout
{
    public DockLayout Left { get; set; } = new() { Width = 300 };
    public DockLayout Right { get; set; } = new() { Width = 330 };

    public EditorWorkspaceLayout ValidatedCopy() => new()
    {
        Left = DockLayout.Copy(Left, 300), Right = DockLayout.Copy(Right, 330)
    };
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
                Id = p.Id, IsOpen = p.IsOpen,
                Weight = double.IsFinite(p.Weight) ? Math.Clamp(p.Weight, .1, 10) : 1
            }).ToList()
    };
}

public sealed class WorkspacePanelLayout
{
    public string Id { get; set; } = string.Empty;
    public bool IsOpen { get; set; } = true;
    public double Weight { get; set; } = 1;
}
