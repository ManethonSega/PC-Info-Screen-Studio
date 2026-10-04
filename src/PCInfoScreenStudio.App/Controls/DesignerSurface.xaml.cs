using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SkiaSharp;
using PCInfoScreenStudio.Rendering;
using PCInfoScreenStudio.ViewModels;

namespace PCInfoScreenStudio.Controls;

public partial class DesignerSurface : System.Windows.Controls.UserControl
{
    private readonly ThemeRenderer _renderer = new();
    private MainViewModel? _viewModel;
    private bool _renderQueued;
    private bool _sendLivePending;
    private bool _forceSendPending;

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
        UpdateGridOverlay();
    }

    private void OnThemeChanged(object? sender, EventArgs e)
        => QueueRender(_viewModel?.LivePreview == true, forceSend: false);

    private void OnRequestLiveFrame(object? sender, EventArgs e)
        => QueueRender(sendLive: true, forceSend: true);

    private void QueueRender(bool sendLive, bool forceSend)
    {
        _sendLivePending |= sendLive;
        _forceSendPending |= forceSend;

        if (_renderQueued)
            return;

        _renderQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
        {
            _renderQueued = false;
            var shouldSend = _sendLivePending;
            var shouldForce = _forceSendPending;
            _sendLivePending = false;
            _forceSendPending = false;

            UpdateGridOverlay();
            RenderPreview(shouldSend, shouldForce);
        });
    }

    private void RenderPreview(bool sendLive, bool forceSend = false)
    {
        if (_viewModel is null)
        {
            RenderSurface.Source = null;
            return;
        }

        using var bitmap = _renderer.Render(_viewModel.Workspace);
        RenderSurface.Source = ToBitmapSource(bitmap);

        if (sendLive && (_viewModel.LivePreview || forceSend))
            _viewModel.SendLiveFrame(bitmap, forceSend);
    }


    private void UpdateGridOverlay()
    {
        GridOverlay.Children.Clear();

        if (_viewModel is null || !_viewModel.Document.EditorGridVisible)
            return;

        var spacing = Math.Clamp(_viewModel.Document.GridSize, 2, 100);
        var width = _viewModel.Document.CanvasWidth;
        var height = _viewModel.Document.CanvasHeight;
        var brush = new SolidColorBrush(Color.FromArgb(72, 150, 160, 175));
        brush.Freeze();

        for (double x = spacing; x < width; x += spacing)
        {
            GridOverlay.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = 0,
                Y2 = height,
                Stroke = brush,
                StrokeThickness = 0.45,
                SnapsToDevicePixels = true
            });
        }

        for (double y = spacing; y < height; y += spacing)
        {
            GridOverlay.Children.Add(new Line
            {
                X1 = 0,
                X2 = width,
                Y1 = y,
                Y2 = y,
                Stroke = brush,
                StrokeThickness = 0.45,
                SnapsToDevicePixels = true
            });
        }
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
