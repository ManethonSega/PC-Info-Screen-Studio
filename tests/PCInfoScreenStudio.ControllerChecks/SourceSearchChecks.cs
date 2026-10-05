using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PCInfoScreenStudio.Controllers;
using PCInfoScreenStudio.Controls;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;
using PCInfoScreenStudio.ViewModels;

internal static class SourceSearchChecks
{
    public static void Check(string root, Action<Func<bool>> pump)
    {
        var settings = new SettingsController(new AppSettingsService(Path.Combine(root, "source-search.json")))
        { FirstRunCompleted = true, StartWithWindows = false, AutoStartDisplay = false, CloseToTray = false };
        var vm = new MainViewModel(settings, new SessionStateService(Path.Combine(root, "source-search-session")));
        vm.SetEditorActive(false);
        var window = new PCInfoScreenStudio.MainWindow(vm);
        try
        {
            window.Show();
            pump(() => window.IsEnabled);
            vm.SetEditorActive(false);
            var original = vm.SelectedWidget!;
            var originalSource = original.DataSource;
            vm.AddWidgetCommand.Execute("Value");
            var widget = vm.SelectedWidget!;
            widget.DataSource = "CPU.Usage";
            var count = vm.Document.Widgets.Count;
            Flush();
            var search = (TextBox)window.FindName("SourceSearchBox");
            var picker = Find<SourcePicker>(window)!;
            Assert(picker.SelectedSource == "CPU.Usage", "Source must bind to the selected widget.");
            search.SetCurrentValue(TextBox.TextProperty, "gpu");
            search.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Flush();
            Assert(vm.SourceSearchText == "gpu" && picker.Items.Count == 8 &&
                picker.Items.Cast<string>().All(s => s.Contains("GPU", StringComparison.OrdinalIgnoreCase)),
                "Typing GPU in the real search box must filter all GPU sources into the actual dropdown.");
            Assert(widget.DataSource == "CPU.Usage" && picker.Text == "CPU.Usage" && vm.Document.Widgets.Count == count,
                "Filtering out the current source must not erase it or add widgets.");
            picker.IsDropDownOpen = true;
            Flush();
            picker.SelectedItem = "GPU.Power";
            picker.IsDropDownOpen = false;
            Flush();
            Assert(widget.DataSource == "GPU.Power" && original.DataSource == originalSource &&
                ReferenceEquals(vm.SelectedWidget, widget) && vm.Document.Widgets.Count == count,
                "Choosing a result must update only the selected widget's source.");
            var input = (TextBox)picker.Template.FindName("PART_EditableTextBox", picker);
            Assert(input.IsReadOnly, "The source display must stay read-only; searching uses the search box.");
            vm.SourceSearchText = " CPU temperature ";
            Flush();
            Assert(picker.Items.Count == 1 && Equals(picker.Items[0], "CPU.Temperature") && widget.DataSource == "GPU.Power",
                "Multiword search must match dotted source names without changing the current source.");
            vm.SourceSearchText = "no-such-sensor";
            Flush();
            Assert(picker.Items.Count == 0 && widget.DataSource == "GPU.Power" && picker.Text == "GPU.Power",
                "No results must preserve the current source and display it.");
            vm.ClearSourceSearchCommand.Execute(null);
            Flush();
            Assert(picker.Items.Count == vm.DataSources.Count && Equals(picker.SelectedItem, "GPU.Power") && search.Text == "",
                "Clear must restore the full dropdown and its selection.");
            vm.SourceSearchText = "GPU";
            vm.ShowAdvancedSensors = true;
            const string raw = "Sensor: GPU > Test temperature";
            vm.DataSources.Add(raw);
            Flush();
            Assert(picker.Items.Contains(raw), "Newly discovered advanced sources must appear in an active search.");
            vm.ShowAdvancedSensors = false;
            Flush();
            Assert(!picker.Items.Contains(raw) && widget.DataSource == "GPU.Power", "Hiding raw sensors must refresh the filtered dropdown safely.");
            vm.SelectedWidget = original;
            Flush();
            Assert(original.DataSource == originalSource && Find<SourcePicker>(window)!.Text == originalSource,
                "Changing widgets while a search is active must show the new widget's actual source.");
            vm.Document.Mode = ScreenMode.Hybrid;
            vm.AddWidgetCommand.Execute("Value");
            Flush();
            picker = Find<SourcePicker>(window)!;
            picker.SelectedItem = "GPU.VRAM";
            Flush();
            Assert(vm.Document.HybridWidgets.Count == 1 && vm.SelectedWidget!.DataSource == "GPU.VRAM" && vm.Document.Widgets.Count == count,
                "Hybrid source search must configure its overlay without adding extra widgets or changing Info Screen.");
            var theme = Path.Combine(root, "searched-source.pchybrid");
            var modes = new ModeThemeService(root);
            modes.SaveToPath(vm.Document, ScreenMode.Hybrid, theme);
            Assert(modes.Load(theme).HybridWidgets.Single().DataSource == "GPU.VRAM", "The chosen source must survive saving and reopening.");
            Console.WriteLine("PASS: actual WPF source search, all GPU choices, unchanged selection, empty results, Clear, advanced sensors, Hybrid and saved source.");
        }
        finally { window.MarkSessionSavedForExit(); window.Close(); }
    }

    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.ContextIdle);
    private static T? Find<T>(DependencyObject element) where T : DependencyObject
    {
        if (element is T match) return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            if (Find<T>(VisualTreeHelper.GetChild(element, i)) is { } found) return found;
        return null;
    }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
