using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PCInfoScreenStudio.Controls;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;
using PCInfoScreenStudio.Controllers;
using PCInfoScreenStudio.ViewModels;

internal static class EditingRecoveryChecks
{
    public static void Check(string root, Action<Task> awaitTask)
    {
        var settings = new SettingsController(new AppSettingsService(Path.Combine(root, "editing-recovery-settings.json")))
        { FirstRunCompleted = true, AutoStartDisplay = false, StartWithWindows = false, PreferredCanvasSize = DisplayCanvasSize.Turing52 };
        using var vm = new MainViewModel(settings);
        vm.SetEditorActive(false);
        vm.Document.SnapToGrid = false;
        vm.AddWidgetCommand.Execute("Graph");
        var widget = vm.SelectedWidget!;
        widget.X = 50; widget.Y = 40; widget.Width = 600; widget.Height = 200; widget.FontSize = 80;
        var item = new DesignerItemControl { DataContext = widget, Width = 600, Height = 200 };
        var window = new Window { DataContext = vm, Content = item, Width = 800, Height = 400 };
        window.Measure(new Size(800, 400)); window.Arrange(new Rect(0, 0, 800, 400)); window.UpdateLayout();
        var grip = Descendants(item).OfType<Thumb>().Single(t => Equals(t.Tag, "SE"));
        var changes = 0;
        widget.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(WidgetModel.IsSelected)) changes++; };
        typeof(DesignerItemControl).GetMethod("OnResizeStarted", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(item, new object[] { grip, new DragStartedEventArgs(0, 0) });
        Assert(changes == 0 && widget.IsSelected, "Beginning a grip drag must not hide grips by toggling selection.");
        typeof(DesignerItemControl).GetMethod("OnResize", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(item, new object[] { grip, new DragDeltaEventArgs(120, 80) });
        Assert(widget.Width == 720 && widget.Height == 280 && widget.FontSize == 80, "Actual WPF grip resizing must work at font 80 without changing font size.");

        var path = Path.Combine(root, "recovery-dedup.t3theme");
        typeof(MainViewModel).GetField("_recoveryPath", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, path);
        var save = typeof(MainViewModel).GetMethod("SaveRecoveryAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        awaitTask((Task)save.Invoke(vm, null)!);
        Assert(File.Exists(path), "An unsaved edit must create a recovery copy.");
        var sentinel = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, sentinel);
        awaitTask((Task)save.Invoke(vm, null)!);
        Assert(File.GetLastWriteTimeUtc(path) == sentinel, "Repeated recovery ticks without edits must not rewrite the package.");
        widget.Label = "New edit after recovery";
        awaitTask((Task)save.Invoke(vm, null)!);
        Assert(File.GetLastWriteTimeUtc(path) != sentinel, "A new edit must trigger the next recovery write.");
        var load = new ThemePackageService().LoadAsync(path); awaitTask(load);
        using (load.Result) Assert(load.Result.Document.Widgets.Any(w => w.Label == widget.Label), "Recovery must contain the newest frozen edit snapshot.");
        window.Content = null;
        Console.WriteLine("PASS: WPF grip capture/resize at font 80 and edit-only recovery writes.");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static void Assert(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
}
