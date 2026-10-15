using System.Windows;
using System.Windows.Controls;

namespace PCInfoScreenStudio.Controls;

public sealed class WorkspacePanel : ContentControl
{
    public string PanelId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public bool IsScrollable { get; set; } = true;
    public double Weight { get; set; } = 1;
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(WorkspacePanel), new FrameworkPropertyMetadata(true,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public bool IsOpen { get => (bool)GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    internal event EventHandler? StateChanged;

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsOpenProperty || e.Property == VisibilityProperty)
            StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
