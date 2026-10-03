using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.ViewModels;

namespace PCInfoScreenStudio.Controls;

public partial class DesignerItemControl : System.Windows.Controls.UserControl
{
    private const double MinSize = 8;

    public DesignerItemControl() => InitializeComponent();

    private WidgetModel? Widget => DataContext as WidgetModel;
    private MainViewModel? ViewModel => Window.GetWindow(this)?.DataContext as MainViewModel;

    private void OnSelect(object sender, MouseButtonEventArgs e)
    {
        if (Widget is null) return;
        ViewModel?.SelectWidget(Widget);
        e.Handled = false;
    }

    private void OnDragStarted(object sender, DragStartedEventArgs e)
    {
        if (Widget is not null) ViewModel?.SelectWidget(Widget);
    }

    private void OnMove(object sender, DragDeltaEventArgs e)
    {
        var w = Widget;
        var vm = ViewModel;
        if (w is null || vm is null || w.IsLocked) return;

        w.X = Math.Clamp(w.X + e.HorizontalChange, 0, Math.Max(0, vm.Document.CanvasWidth - w.Width));
        w.Y = Math.Clamp(w.Y + e.VerticalChange, 0, Math.Max(0, vm.Document.CanvasHeight - w.Height));
        vm.NotifyDesignerChange();
    }

    private void OnResize(object sender, DragDeltaEventArgs e)
    {
        var w = Widget;
        var vm = ViewModel;
        if (w is null || vm is null || w.IsLocked || sender is not Thumb thumb) return;

        var edge = thumb.Tag?.ToString() ?? string.Empty;
        var x = w.X;
        var y = w.Y;
        var width = w.Width;
        var height = w.Height;

        if (edge.Contains('E')) width = Math.Max(MinSize, width + e.HorizontalChange);
        if (edge.Contains('S')) height = Math.Max(MinSize, height + e.VerticalChange);
        if (edge.Contains('W'))
        {
            var delta = Math.Min(e.HorizontalChange, width - MinSize);
            x += delta;
            width -= delta;
        }
        if (edge.Contains('N'))
        {
            var delta = Math.Min(e.VerticalChange, height - MinSize);
            y += delta;
            height -= delta;
        }

        width = Math.Min(width, vm.Document.CanvasWidth - x);
        height = Math.Min(height, vm.Document.CanvasHeight - y);
        x = Math.Clamp(x, 0, vm.Document.CanvasWidth - MinSize);
        y = Math.Clamp(y, 0, vm.Document.CanvasHeight - MinSize);

        w.X = x;
        w.Y = y;
        w.Width = Math.Max(MinSize, width);
        w.Height = Math.Max(MinSize, height);
        vm.NotifyDesignerChange();
    }
}
