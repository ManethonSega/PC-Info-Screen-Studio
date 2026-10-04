using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Input;
using SkiaSharp;
using PCInfoScreenStudio.Rendering;
using PCInfoScreenStudio.ViewModels;
using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Controls;

public partial class DesignerSurface : System.Windows.Controls.UserControl
{
    private readonly ThemeRenderer _renderer = new();
    private MainViewModel? _viewModel;
    private bool _renderQueued;
    private bool _sendLivePending;
    private bool _forceSendPending;
    private Point? _marqueeStart;
    private bool _marqueeAdditive;

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
            _viewModel.AlignmentGuidesChanged -= OnAlignmentGuidesChanged;
        }

        _viewModel = e.NewValue as MainViewModel;
        if (_viewModel is not null)
        {
            _viewModel.ThemeChanged += OnThemeChanged;
            _viewModel.RequestLiveFrame += OnRequestLiveFrame;
            _viewModel.AlignmentGuidesChanged += OnAlignmentGuidesChanged;
        }

        RenderPreview(sendLive: false);
        UpdateGridOverlay();
        UpdateAlignmentGuides();
    }

    private void OnThemeChanged(object? sender, EventArgs e)
        => QueueRender(_viewModel?.LivePreview == true, forceSend: false);

    private void OnRequestLiveFrame(object? sender, EventArgs e)
        => QueueRender(sendLive: true, forceSend: true);

    private void OnAlignmentGuidesChanged(object? sender, EventArgs e)
        => UpdateAlignmentGuides();

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

        if (_viewModel is null ||
            _viewModel.Document.Mode == ScreenMode.PhotoFrame ||
            !_viewModel.Document.EditorGridVisible)
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

    private void UpdateAlignmentGuides()
    {
        if (_viewModel?.AlignmentGuideX is double x)
        {
            VerticalGuide.X1 = x;
            VerticalGuide.X2 = x;
            VerticalGuide.Y1 = 0;
            VerticalGuide.Y2 = _viewModel.Document.CanvasHeight;
            VerticalGuide.Visibility = Visibility.Visible;
        }
        else
        {
            VerticalGuide.Visibility = Visibility.Collapsed;
        }

        if (_viewModel?.AlignmentGuideY is double y)
        {
            HorizontalGuide.X1 = 0;
            HorizontalGuide.X2 = _viewModel.Document.CanvasWidth;
            HorizontalGuide.Y1 = y;
            HorizontalGuide.Y2 = y;
            HorizontalGuide.Visibility = Visibility.Visible;
        }
        else
        {
            HorizontalGuide.Visibility = Visibility.Collapsed;
        }
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel is null ||
            _viewModel.Document.Mode == ScreenMode.PhotoFrame ||
            FindAncestor<DesignerItemControl>(e.OriginalSource as DependencyObject) is not null)
            return;

        _marqueeStart = e.GetPosition(EditorCanvas);
        _marqueeAdditive = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        Canvas.SetLeft(SelectionMarquee, _marqueeStart.Value.X);
        Canvas.SetTop(SelectionMarquee, _marqueeStart.Value.Y);
        SelectionMarquee.Width = 0;
        SelectionMarquee.Height = 0;
        SelectionMarquee.Visibility = Visibility.Visible;
        EditorCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (_marqueeStart is not Point start || e.LeftButton != MouseButtonState.Pressed)
            return;

        var current = e.GetPosition(EditorCanvas);
        Canvas.SetLeft(SelectionMarquee, Math.Min(start.X, current.X));
        Canvas.SetTop(SelectionMarquee, Math.Min(start.Y, current.Y));
        SelectionMarquee.Width = Math.Abs(current.X - start.X);
        SelectionMarquee.Height = Math.Abs(current.Y - start.Y);
        e.Handled = true;
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_marqueeStart is not Point start || _viewModel is null)
            return;

        var end = e.GetPosition(EditorCanvas);
        var selection = new Rect(start, end);
        var matches = selection.Width < 3 && selection.Height < 3
            ? Array.Empty<WidgetModel>()
            : _viewModel.Document.EditorWidgets
                .Where(w => w.IsVisible && selection.IntersectsWith(new Rect(w.X, w.Y, w.Width, w.Height)))
                .ToArray();

        _viewModel.SelectWidgets(matches, _marqueeAdditive);
        SelectionMarquee.Visibility = Visibility.Collapsed;
        _marqueeStart = null;
        EditorCanvas.ReleaseMouseCapture();
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
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
