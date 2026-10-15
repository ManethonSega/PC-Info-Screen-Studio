using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PCInfoScreenStudio.Controls;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;
using PCInfoScreenStudio.ViewModels;

internal static class UiPreviews
{
    // Capture the actual WPF controls for layout review, without a USB device.
    public static void Capture(string directory)
    {
        Directory.CreateDirectory(directory);
        CaptureElement(UiStateChecks.Gallery(), 660, 460, Path.Combine(directory, "Control-states.png"));
        var window = new PCInfoScreenStudio.MainWindow();
        var vm = (MainViewModel)window.DataContext;
        vm.SetEditorActive(false);
        vm.CompleteFirstRunCommand.Execute(null);
        vm.SetEditorActive(true);
        var root = (Grid)window.Content;
        try
        {
            foreach (var mode in new[] { ScreenMode.InfoScreen, ScreenMode.PhotoFrame, ScreenMode.Hybrid })
            {
                vm.Document.Mode = mode;
                FlushBindings();
                CaptureElement(root, 1560, 1040, Path.Combine(directory, mode + ".png"));
                CaptureElement(root, 1080, 680, Path.Combine(directory, mode + "-compact.png"));
            }
            vm.ShowPage(true);
            vm.HardwareDashboard.Update(new Dictionary<string, MetricValue>
            {
                ["Hardware.CPUName"] = new(Text: "Sample processor"), ["CPU.Usage"] = new(24, Unit: "%"),
                ["CPU.Temperature"] = new(48, Unit: "°C"), ["CPU.Clock"] = new(4200, Unit: "MHz"), ["CPU.Power"] = new(52, Unit: "W"),
                ["Hardware.GPUName"] = new(Text: "Sample graphics adapter"), ["GPU.Usage"] = new(18, Unit: "%"), ["GPU.Temperature"] = new(42, Unit: "°C"),
                ["RAM.TotalGB"] = new(32, Unit: "GB"), ["RAM.UsedGB"] = new(8.4, Unit: "GB"), ["RAM.AvailableGB"] = new(23.6, Unit: "GB"), ["RAM.Usage"] = new(26.3, Unit: "%"),
                ["Hardware.StorageNames"] = new(Text: "Sample solid-state drive"), ["Disk.Usage"] = new(38, Unit: "%"), ["Disk.FreeGB"] = new(620, Unit: "GB"),
                ["Network.AdapterNames"] = new(Text: "Sample Ethernet adapter"), ["Network.Download"] = new(2.3, Unit: "MB/s"), ["Network.Upload"] = new(0.4, Unit: "MB/s")
            });
            CaptureElement(root, 1560, 1040, Path.Combine(directory, "Hardware.png"));
            var settingsWindow = new PCInfoScreenStudio.SettingsWindow { DataContext = vm };
            CaptureElement((FrameworkElement)settingsWindow.Content, 720, 680, Path.Combine(directory, "Settings.png"));
            CapturePanel((Border)settingsWindow.FindName("RuntimePanel"), vm, 660, 250,
                Path.Combine(directory, "Windows-startup.png"));
            CapturePanel((Border)settingsWindow.FindName("HardwareSensorsPanel"), vm, 660, 230,
                Path.Combine(directory, "Sensor-startup.png"));
            vm.ShowPage(false);
            vm.Document.Mode = ScreenMode.InfoScreen;
            ((WorkspaceDock)window.FindName("LeftDock")).Panels.Single(p => p.PanelId == "themes").IsOpen = true;
            root.UpdateLayout();
            var panel = Find<ThemeLibraryPanel>(root) ?? throw new InvalidOperationException("Theme panel missing.");
            var actions = Find<Button>(panel, b => b.ContextMenu is not null) ?? throw new InvalidOperationException("Theme actions missing.");
            var menu = actions.ContextMenu!;
            menu.DataContext = vm;
            menu.ApplyTemplate();
            CaptureElement(menu, 360, 310, Path.Combine(directory, "Theme-actions.png"));
            vm.Document.Mode = ScreenMode.Hybrid;
            FlushBindings();
            vm.AddWidgetCommand.Execute("Shape");
            vm.SelectedWidget!.BackgroundColor = "#FF20242B";
            vm.SelectedWidget.BackgroundTransparency = 40;
            vm.Document.PhotoFrame.CaptionFontFamily = "Orbitron";
            FlushBindings();
            root.UpdateLayout();
            var dock = (WorkspaceDock)window.FindName("LeftDock");
            var captions = (FrameworkElement)dock.Panels.Single(p => p.PanelId == "captions").Content;
            var slideshow = (FrameworkElement)dock.Panels.Single(p => p.PanelId == "slideshow").Content;
            var overlays = (FrameworkElement)dock.Panels.Single(p => p.PanelId == "overlays").Content;
            var sources = (FrameworkElement)dock.Panels.Single(p => p.PanelId == "sources").Content;
            var colours = Find<Expander>(root, e => Equals(e.Header, "Colours"))!;
            if (colours.DataContext is not WidgetModel { Type: WidgetType.Shape, BackgroundTransparency: 40 })
                throw new InvalidOperationException("Appearance preview did not bind the selected Shape and transparency.");
            CapturePanel(captions, vm, 310, 420, Path.Combine(directory, "Caption-settings.png"));
            CapturePanel(slideshow, vm, 310, 300, Path.Combine(directory, "Slideshow-settings.png"));
            CapturePanel(sources, vm, 310, 180, Path.Combine(directory, "Photo-sources.png"));
            CapturePanel(overlays, vm, 310, 290, Path.Combine(directory, "Hybrid-overlays.png"));
            CapturePanel(colours, vm, 310, 260, Path.Combine(directory, "Widget-background.png"));
            vm.Document.Mode = ScreenMode.InfoScreen;
            vm.AddWidgetCommand.Execute("HardwareName");
            vm.SelectedWidget!.HardwareNameText = "Intel Core i9-13900K 👌💕👏";
            vm.SelectedWidget.X = 8; vm.SelectedWidget.Y = 4; vm.SelectedWidget.Width = 460; vm.SelectedWidget.Height = 44;
            FlushBindings();
            CaptureElement(root, 1560, 1040, Path.Combine(directory, "Hardware-name-widget.png"));
            CapturePanel((Border)settingsWindow.FindName("DiagnosticsPanel"), vm, 660, 175,
                Path.Combine(directory, "Export-diagnostics.png"));
            var export = Find<Button>((FrameworkElement)settingsWindow.Content, b => Equals(b.Content, "Export diagnostics"));
            if (export is null || !ReferenceEquals(export.Command, vm.ExportDiagnosticsCommand)) throw new InvalidOperationException("Diagnostics button is not bound.");
            vm.AutoStartDisplay = false;
            vm.CanvasSize = DisplayCanvasSize.Turing52;
            vm.Document.Widgets.Clear();
            vm.ImportMediaFile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "video-colours.mp4"), ThemeAssetKind.Video);
            vm.SelectedWidget!.MediaPaused = true; vm.MediaPanelExpanded = true;
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                using var frame = PCInfoScreenStudio.Rendering.VideoFrameProvider.GetFrame(vm.Workspace, vm.SelectedWidget, vm.CanvasSize, 0, out _);
                if (frame is not null) break;
                if (deadline.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Video UI capture failed to decode.");
                FlushBindings(); Thread.Sleep(5);
            }
            vm.NotifyDesignerChange(); FlushBindings();
            CaptureElement(root, 1560, 1040, Path.Combine(directory, "Video-playback.png"));
            // Exercise native startup sizing, including monitor/DPI detection, without showing the editor.
            _ = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
        }
        finally { vm.Dispose(); }
    }

    private static void FlushBindings()
        => System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
            new Action(() => { }), System.Windows.Threading.DispatcherPriority.ContextIdle);

    private static void CapturePanel(FrameworkElement element, MainViewModel vm, int width, int height, string path)
    {
        var parent = LogicalTreeHelper.GetParent(element);
        var parentPanel = parent as Panel;
        var parentContent = parent as ContentControl;
        var index = parentPanel?.Children.IndexOf(element) ?? -1;
        if (index < 0 && parentContent?.Content != element)
            throw new InvalidOperationException("Capture content must belong to a panel or content control.");
        var context = element.DataContext;
        var inheritedContext = element.ReadLocalValue(FrameworkElement.DataContextProperty) == DependencyProperty.UnsetValue;
        if (index >= 0) parentPanel!.Children.RemoveAt(index);
        else parentContent!.Content = null;
        element.DataContext = context;
        var host = new Window { DataContext = vm };
        var background = new Border { Background = (Brush)Application.Current.Resources["PanelBrush"], Child = element };
        host.Content = background;
        try
        {
            FlushBindings();
            CaptureElement(background, width, height, path);
            if (context is WidgetModel widget && Find<Slider>(element) is { } slider &&
                slider.Value != widget.BackgroundTransparency)
                throw new InvalidOperationException("The captured background percentage did not match the widget.");
        }
        finally
        {
            background.Child = null;
            host.Content = null;
            if (index >= 0) parentPanel!.Children.Insert(index, element);
            else parentContent!.Content = element;
            if (inheritedContext) element.ClearValue(FrameworkElement.DataContextProperty);
        }
    }

    private static void CaptureElement(FrameworkElement element, int width, int height, string path)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static T? Find<T>(DependencyObject parent, Func<T, bool>? predicate = null) where T : DependencyObject
    {
        if (parent is T match && (predicate is null || predicate(match))) return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (Find(VisualTreeHelper.GetChild(parent, i), predicate) is { } result) return result;
        return null;
    }
}
