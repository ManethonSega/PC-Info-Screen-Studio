using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.ViewModels;

namespace PCInfoScreenStudio.Controls;

public partial class DesignerItemControl : System.Windows.Controls.UserControl
{
    private const double MinSize = 8;

    private double _moveStartX;
    private double _moveStartY;
    private double _moveTotalX;
    private double _moveTotalY;

    private double _resizeStartX;
    private double _resizeStartY;
    private double _resizeStartWidth;
    private double _resizeStartHeight;
    private double _resizeTotalX;
    private double _resizeTotalY;

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
        var w = Widget;
        if (w is null) return;

        ViewModel?.SelectWidget(w);
        _moveStartX = w.X;
        _moveStartY = w.Y;
        _moveTotalX = 0;
        _moveTotalY = 0;
    }

    private void OnMove(object sender, DragDeltaEventArgs e)
    {
        var w = Widget;
        var vm = ViewModel;
        if (w is null || vm is null || w.IsLocked) return;

        _moveTotalX += e.HorizontalChange;
        _moveTotalY += e.VerticalChange;

        var x = _moveStartX + _moveTotalX;
        var y = _moveStartY + _moveTotalY;

        if (vm.Document.SnapToGrid)
        {
            x = Snap(x, vm.Document.GridSize);
            y = Snap(y, vm.Document.GridSize);
        }

        w.X = Math.Clamp(x, 0, Math.Max(0, vm.Document.CanvasWidth - w.Width));
        w.Y = Math.Clamp(y, 0, Math.Max(0, vm.Document.CanvasHeight - w.Height));
        vm.NotifyDesignerChange();
    }

    private void OnResizeStarted(object sender, DragStartedEventArgs e)
    {
        var w = Widget;
        if (w is null) return;

        ViewModel?.SelectWidget(w);
        _resizeStartX = w.X;
        _resizeStartY = w.Y;
        _resizeStartWidth = w.Width;
        _resizeStartHeight = w.Height;
        _resizeTotalX = 0;
        _resizeTotalY = 0;
    }

    private void OnResize(object sender, DragDeltaEventArgs e)
    {
        var w = Widget;
        var vm = ViewModel;
        if (w is null || vm is null || w.IsLocked || sender is not Thumb thumb) return;

        _resizeTotalX += e.HorizontalChange;
        _resizeTotalY += e.VerticalChange;

        var edge = thumb.Tag?.ToString() ?? string.Empty;
        var x = _resizeStartX;
        var y = _resizeStartY;
        var width = _resizeStartWidth;
        var height = _resizeStartHeight;

        if (edge.Contains('E'))
            width = Math.Max(MinSize, _resizeStartWidth + _resizeTotalX);

        if (edge.Contains('S'))
            height = Math.Max(MinSize, _resizeStartHeight + _resizeTotalY);

        if (edge.Contains('W'))
        {
            var right = _resizeStartX + _resizeStartWidth;
            x = Math.Min(right - MinSize, _resizeStartX + _resizeTotalX);
            width = right - x;
        }

        if (edge.Contains('N'))
        {
            var bottom = _resizeStartY + _resizeStartHeight;
            y = Math.Min(bottom - MinSize, _resizeStartY + _resizeTotalY);
            height = bottom - y;
        }

        if (vm.Document.SnapToGrid)
        {
            var grid = vm.Document.GridSize;

            if (edge.Contains('W'))
            {
                var right = x + width;
                x = Snap(x, grid);
                width = right - x;
            }
            else if (edge.Contains('E'))
            {
                width = Snap(width, grid);
            }

            if (edge.Contains('N'))
            {
                var bottom = y + height;
                y = Snap(y, grid);
                height = bottom - y;
            }
            else if (edge.Contains('S'))
            {
                height = Snap(height, grid);
            }
        }

        x = Math.Clamp(x, 0, Math.Max(0, vm.Document.CanvasWidth - MinSize));
        y = Math.Clamp(y, 0, Math.Max(0, vm.Document.CanvasHeight - MinSize));
        width = Math.Clamp(width, MinSize, vm.Document.CanvasWidth - x);
        height = Math.Clamp(height, MinSize, vm.Document.CanvasHeight - y);

        w.X = x;
        w.Y = y;
        w.Width = width;
        w.Height = height;
        vm.NotifyDesignerChange();
    }

    private static double Snap(double value, double spacing)
    {
        spacing = Math.Clamp(spacing, 2, 100);
        return Math.Round(value / spacing, MidpointRounding.AwayFromZero) * spacing;
    }
}
