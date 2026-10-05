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
        root.Background = new SolidColorBrush(Color.FromRgb(16, 18, 22));
        try
        {
            foreach (var mode in new[] { ScreenMode.InfoScreen, ScreenMode.PhotoFrame, ScreenMode.Hybrid })
            {
                vm.Document.Mode = mode;
                CaptureElement(root, 1480, 810, Path.Combine(directory, mode + ".png"));
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
            CaptureElement(root, 1480, 810, Path.Combine(directory, "Hardware.png"));
            vm.ShowPage(false);
            vm.Document.Mode = ScreenMode.InfoScreen;
            root.UpdateLayout();
            var panel = Find<ThemeLibraryPanel>(root) ?? throw new InvalidOperationException("Theme panel missing.");
            var actions = Find<Button>(panel, b => b.ContextMenu is not null) ?? throw new InvalidOperationException("Theme actions missing.");
            var menu = actions.ContextMenu!;
            menu.DataContext = vm;
            menu.ApplyTemplate();
            CaptureElement(menu, 360, 310, Path.Combine(directory, "Theme-actions.png"));
        }
        finally { vm.Dispose(); }
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
