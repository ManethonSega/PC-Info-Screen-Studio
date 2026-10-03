using System.Windows;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using PCInfoScreenStudio.Rendering;
using PCInfoScreenStudio.ViewModels;

namespace PCInfoScreenStudio.Controls;

public partial class DesignerSurface : System.Windows.Controls.UserControl
{
    private readonly ThemeRenderer _renderer = new();
    private MainViewModel? _viewModel;

    public DesignerSurface()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.ThemeChanged -= OnThemeChanged;
            _viewModel.RequestLiveFrame -= OnRequestLiveFrame;
        }

        _viewModel = e.NewValue as MainViewModel;
        if (_viewModel is not null)
        {
            _viewModel.ThemeChanged += OnThemeChanged;
            _viewModel.RequestLiveFrame += OnRequestLiveFrame;
        }

        RenderSurface?.InvalidateVisual();
    }

    private void OnThemeChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(() => RenderSurface.InvalidateVisual());

    private void OnRequestLiveFrame(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(RenderAndSend);

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        if (_viewModel is null)
        {
            e.Surface.Canvas.Clear(SKColors.Black);
            return;
        }

        using var bitmap = _renderer.Render(_viewModel.Workspace);
        e.Surface.Canvas.Clear(SKColors.Black);
        e.Surface.Canvas.DrawBitmap(bitmap, new SKRect(0, 0, e.Info.Width, e.Info.Height));

        if (_viewModel.LivePreview)
            _viewModel.SendLiveFrame(bitmap);
    }

    private void RenderAndSend()
    {
        if (_viewModel is null) return;

        using var bitmap = _renderer.Render(_viewModel.Workspace);
        _viewModel.SendLiveFrame(bitmap);
    }
}
