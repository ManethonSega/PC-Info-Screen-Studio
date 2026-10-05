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
        var window = new PCInfoScreenStudio.MainWindow();
        var vm = (MainViewModel)window.DataContext;
        vm.SetEditorActive(false);
        vm.CompleteFirstRunCommand.Execute(null);
        var root = (Grid)window.Content;
        try
        {
            foreach (var mode in new[] { ScreenMode.InfoScreen, ScreenMode.PhotoFrame, ScreenMode.Hybrid })
            {
                vm.Document.Mode = mode;
                CaptureElement(root, 1560, 1040, Path.Combine(directory, mode + ".png"));
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
            CapturePanel((Border)settingsWindow.FindName("HardwareSensorsPanel"), vm, 660, 230,
                Path.Combine(directory, "Sensor-startup.png"));
            vm.ShowPage(false);
            vm.Document.Mode = ScreenMode.InfoScreen;
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
            var album = Find<Expander>(root, e => Equals(e.Header, "ALBUM SETTINGS"))!;
            var overlays = Find<Expander>(root, e => Equals(e.Header, "HYBRID OVERLAYS"))!;
            var colours = Find<Expander>(root, e => Equals(e.Header, "COLOURS"))!;
            if (colours.DataContext is not WidgetModel { Type: WidgetType.Shape, BackgroundTransparency: 40 })
                throw new InvalidOperationException("Appearance preview did not bind the selected Shape and transparency.");
            CapturePanel(album, vm, 310, 590, Path.Combine(directory, "Caption-settings.png"));
            CapturePanel(overlays, vm, 310, 225, Path.Combine(directory, "Hybrid-overlays.png"));
            CapturePanel(colours, vm, 310, 210, Path.Combine(directory, "Widget-background.png"));
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
        var parent = (Panel)VisualTreeHelper.GetParent(element);
        var index = parent.Children.IndexOf(element);
        var context = element.DataContext;
        var inheritedContext = element.ReadLocalValue(FrameworkElement.DataContextProperty) == DependencyProperty.UnsetValue;
        parent.Children.Remove(element);
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
            parent.Children.Insert(index, element);
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
