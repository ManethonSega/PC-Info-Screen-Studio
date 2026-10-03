using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;
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
        Loaded += (_, _) => RenderPreview(sendLive: false);
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

        RenderPreview(sendLive: false);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(() => RenderPreview(_viewModel?.LivePreview == true));

    private void OnRequestLiveFrame(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(() => RenderPreview(sendLive: true));

    private void RenderPreview(bool sendLive)
    {
        if (_viewModel is null)
        {
            RenderSurface.Source = null;
            return;
        }

        using var bitmap = _renderer.Render(_viewModel.Workspace);
        RenderSurface.Source = ToBitmapSource(bitmap);

        if (sendLive && _viewModel.LivePreview)
            _viewModel.SendLiveFrame(bitmap);
    }

    private static BitmapSource ToBitmapSource(SKBitmap bitmap)
    {
        var source = BitmapSource.Create(
            bitmap.Width,
            bitmap.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            bitmap.GetPixels(),
            bitmap.RowBytes * bitmap.Height,
            bitmap.RowBytes);

        source.Freeze();
        return source;
    }
}
