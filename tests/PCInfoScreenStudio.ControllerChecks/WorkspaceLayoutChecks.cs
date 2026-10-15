using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PCInfoScreenStudio.Controllers;
using PCInfoScreenStudio.Controls;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;
using PCInfoScreenStudio.ViewModels;

internal static class WorkspaceLayoutChecks
{
    public static void Check(string root, Action<Func<bool>> pump, string? captures)
    {
        var path = Path.Combine(root, "workspace-layout.json");
        var settings = new SettingsController(new AppSettingsService(path))
        { FirstRunCompleted = true, CloseToTray = false, StartWithWindows = false, AutoStartDisplay = false };
        var vm = new MainViewModel(settings, new SessionStateService(Path.Combine(root, "workspace-layout-session")));
        vm.SetEditorActive(false);
        var window = new PCInfoScreenStudio.MainWindow(vm);
        try
        {
            window.Show(); pump(() => window.IsEnabled); vm.SetEditorActive(false);
            window.Width = 1560; window.Height = 1040; Flush(window);
            var left = (WorkspaceDock)window.FindName("LeftDock");
            var right = (WorkspaceDock)window.FindName("RightDock");
            var canvasArea = (Grid)window.FindName("CanvasArea");
            var designer = (DesignerSurface)window.FindName("Designer");
            var layers = left.Panels.Single(p => p.PanelId == "layers");
            var media = left.Panels.Single(p => p.PanelId == "media");
            var properties = right.Panels.Single(p => p.PanelId == "properties");
            Require(layers.IsOpen && properties.IsOpen && !media.IsOpen && vm.IsCanvasFit,
                "Defaults expose Layers and Properties with Fit active and optional media collapsed.");
            Require(All<Button>(left.Panels.Single(p => p.PanelId == "widgets")).All(b => b.ActualHeight <= 42),
                "Direct Add buttons retain compact heights instead of stretching to fill a panel.");
            var content = window.FindName("WidgetProperties");
            vm.CanvasSize = DisplayCanvasSize.Turing52; Flush(window);
            var original = vm.SelectedWidget!;
            var geometry = (original.X, original.Y, original.Width, original.Height);
            var dirty = vm.IsDirty;
            CheckFits(designer);
            Capture(window, captures, "Workspace-docked.png");
            var initialWidth = canvasArea.ActualWidth;
            var initialScale = vm.CanvasViewScale;

            // Real UI Automation toggle peers exercise the same path as mouse/Space activation.
            foreach (var tab in All<ToggleButton>(left).Concat(All<ToggleButton>(right))
                         .Where(t => AutomationProperties.GetName(t).EndsWith(" panel", StringComparison.Ordinal)).ToArray())
                if (tab.IsChecked == true && tab.IsVisible) Toggle(tab);
            Flush(window);
            Require(Math.Abs(left.ActualWidth - WorkspaceDock.RailWidth) < 1 && Math.Abs(right.ActualWidth - WorkspaceDock.RailWidth) < 1,
                "Closing all sections leaves only labelled rails.");
            Require(canvasArea.ActualWidth > initialWidth + 400 && vm.CanvasViewScale > initialScale,
                "Collapsing dock content expands the centre and recomputes Fit.");
            CheckFits(designer); Capture(window, captures, "Workspace-collapsed.png");

            Toggle(Tab(left, "Layers panel")); Toggle(Tab(left, "Media panel")); Toggle(Tab(right, "Properties panel"));
            Flush(window);
            Require(layers.IsOpen && media.IsOpen && properties.IsOpen && All<GridSplitter>(left).Count() == 1,
                "Several independently open sections stack with a resizable divider.");
            Require(ReferenceEquals(content, window.FindName("WidgetProperties")) && ReferenceEquals(vm.SelectedWidget, original),
                "Collapsing and reopening must retain property controls and selection.");
            var firstOrder = left.CaptureLayout().Panels.Select(p => p.Id).ToArray();
            Invoke(All<Button>(left).Single(b => AutomationProperties.GetName(b) == "Move layers panel up"));
            Flush(window);
            Require(!firstOrder.SequenceEqual(left.CaptureLayout().Panels.Select(p => p.Id)), "The panel-up button changes stack order.");
            var splitter = All<GridSplitter>(left).Single();
            var beforeWeights = left.CaptureLayout().Panels.Select(p => p.Weight).ToArray();
            splitter.Focus();
            splitter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Down)
                { RoutedEvent = Keyboard.KeyDownEvent });
            splitter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Down)
                { RoutedEvent = Keyboard.PreviewKeyUpEvent });
            Flush(window);
            Require(!beforeWeights.SequenceEqual(left.CaptureLayout().Panels.Select(p => p.Weight)),
                "Keyboard resizing changes and persists panel proportions.");
            var grip = All<Thumb>(right).Single(t => AutomationProperties.GetName(t) == "Right panel width");
            var range = (IRangeValueProvider)FrameworkElementAutomationPeer.CreatePeerForElement(grip)!.GetPattern(PatternInterface.RangeValue)!;
            range.SetValue(390); Flush(window);
            Require(Math.Abs(right.PreferredPanelWidth - 390) < .1, "Panel width supports an accessible range control.");
            window.Width = 1080; window.Height = 680; Flush(window);
            Require(canvasArea.ActualWidth >= 319 && right.PreferredPanelWidth == 390 && right.EffectivePanelWidth < 390,
                "Compact windows reserve canvas space without replacing the preferred panel width.");
            CheckFits(designer); Capture(window, captures, "Workspace-stacked-compact.png");
            Require((original.X, original.Y, original.Width, original.Height) == geometry && vm.Document.CanvasWidth == 1280 &&
                vm.Document.CanvasHeight == 720 && vm.IsDirty == dirty, "Workspace changes must preserve theme dimensions, widgets and dirty state.");

            Invoke(All<Button>(canvasArea).Single(b => Equals(b.Content, "100%"))); Flush(window);
            Require(!vm.IsCanvasFit && vm.CanvasViewScale == 1, "100% selects manual actual size.");
            vm.CanvasZoom = 1.5; Flush(window);
            media.IsOpen = false; window.Width = 1440; Flush(window);
            Require(vm.CanvasViewScale == 1.5, "Panel and window changes preserve manual zoom.");
            vm.IsCanvasFit = true; Flush(window); CheckFits(designer);
            foreach (var size in new[] { DisplayCanvasSize.Serial35, DisplayCanvasSize.Turing52 })
            {
                vm.CanvasSize = size;
                foreach (var orientation in new[] { ThemeOrientation.Landscape, ThemeOrientation.Portrait })
                {
                    vm.Document.Orientation = orientation; Flush(window); CheckFits(designer);
                }
            }
            vm.Document.Orientation = ThemeOrientation.Landscape;
            vm.Document.Mode = ScreenMode.PhotoFrame; Flush(window);
            Require(!right.IsVisible && left.Panels.Single(p => p.PanelId == "photos").IsVisible && layers.IsOpen,
                "Photo Frame shows its own tools while preserving hidden editor panel preferences.");
            CheckFits(designer);
            vm.Document.Mode = ScreenMode.Hybrid; Flush(window);
            Require(right.IsVisible && layers.IsOpen, "Hybrid restores its contextual editor panels.");
            CheckFits(designer); Capture(window, captures, "Workspace-hybrid.png");
            vm.Document.Mode = ScreenMode.InfoScreen; Flush(window);
            var saved = left.CaptureLayout();
            var restored = new SettingsController(new AppSettingsService(path));
            Require(restored.WorkspaceLayout.Left.Panels.Select(p => (p.Id, p.IsOpen, p.Weight)).SequenceEqual(saved.Panels.Select(p => (p.Id, p.IsOpen, p.Weight))) &&
                restored.WorkspaceLayout.Right.Width == 390 && restored.IsCanvasFit,
                "Saved preferences restore open states, order, heights, widths and Fit.");
            using (var secondVm = new MainViewModel(restored, new SessionStateService(Path.Combine(root, "workspace-layout-second"))))
            {
                var secondWindow = new PCInfoScreenStudio.MainWindow(secondVm);
                try
                {
                    var secondLeft = (WorkspaceDock)secondWindow.FindName("LeftDock");
                    Require(secondLeft.CaptureLayout().Panels.Select(p => (p.Id, p.IsOpen, p.Weight)).SequenceEqual(saved.Panels.Select(p => (p.Id, p.IsOpen, p.Weight))),
                        "A new window actually restores the saved arrangement.");
                }
                finally { secondWindow.MarkSessionSavedForExit(); secondWindow.Close(); }
            }
            var beforeReset = (original.X, original.Y, original.Width, original.Height);
            Invoke(All<Button>(canvasArea).Single(b => Equals(b.Content, "Reset workspace"))); Flush(window);
            Require(vm.IsCanvasFit && left.PreferredPanelWidth == 300 && right.PreferredPanelWidth == 330 &&
                left.CaptureLayout().Panels.Select(p => p.Id).SequenceEqual(left.Panels.Select(p => p.PanelId)) &&
                (original.X, original.Y, original.Width, original.Height) == beforeReset,
                "Reset restores the default workspace without changing artwork.");
            vm.ToggleEditorModeCommand.Execute(null); Flush(window); CheckFits(designer);
            Require(!left.IsVisible && !right.IsVisible, "Live view removes both rails without changing their preferences.");
            vm.ToggleEditorModeCommand.Execute(null); Flush(window);
            CheckPreferenceRepair(root);
            Console.WriteLine("PASS: real WPF dock toggles, stacked resizing/reordering, range accessibility, responsive Fit, manual zoom, both screen families/orientations, mode visibility, persistence and reset.");
        }
        finally { window.MarkSessionSavedForExit(); window.Close(); }
    }

    private static void CheckPreferenceRepair(string root)
    {
        var path = Path.Combine(root, "workspace-layout-repair.json");
        File.WriteAllText(path, """{"SettingsVersion":5,"WeatherCity":"Keep me","UsbBrightness":72,"WorkspaceLayout":{"Left":{"Width":-500,"Panels":[null,{"Id":"layers","Weight":0},{"Id":"layers","Weight":999}]},"Right":null}}""");
        var loaded = new AppSettingsService(path).Load();
        Require(loaded.WeatherCity == "Keep me" && loaded.UsbBrightness == 72 && loaded.WorkspaceLayout.Left.Width == 240 &&
            loaded.WorkspaceLayout.Left.Panels.Count == 1 && loaded.WorkspaceLayout.Left.Panels[0].Weight == .1 && loaded.WorkspaceLayout.Right.Width == 330,
            "Malformed layout fields are repaired without losing unrelated valid preferences.");
    }

    private static void CheckFits(DesignerSurface surface)
    {
        var frame = (Border)surface.FindName("CanvasFrame");
        var bounds = frame.TransformToAncestor(surface).TransformBounds(new Rect(frame.RenderSize));
        Require(bounds.Left >= -.5 && bounds.Top >= -.5 && bounds.Right <= surface.ActualWidth + .5 && bounds.Bottom <= surface.ActualHeight + .5,
            $"Fit must keep the complete physical canvas inside its viewport: {bounds}, viewport {surface.RenderSize}.");
    }
    private static ToggleButton Tab(WorkspaceDock dock, string name) => All<ToggleButton>(dock).Single(t => AutomationProperties.GetName(t) == name);
    private static void Toggle(ToggleButton button) => ((IToggleProvider)FrameworkElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Toggle)!).Toggle();
    private static void Invoke(Button button) => ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)!).Invoke();
    private static void Flush(Window window)
    { Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.ContextIdle); window.UpdateLayout(); }
    private static void Capture(Window window, string? directory, string name)
    {
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        var element = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        using var file = File.Create(Path.Combine(directory, name));
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(file);
    }
    private static IEnumerable<T> All<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in All<T>(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
