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
            CheckContentSizing(window, vm, left, right, captures);
            CheckDynamicContent(pump);
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
            var beforeHeights = left.CaptureLayout().Panels.Select(p => p.PreferredHeight).ToArray();
            splitter.Focus();
            splitter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Down)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            splitter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Down)
                { RoutedEvent = Keyboard.KeyDownEvent });
            splitter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Down)
                { RoutedEvent = Keyboard.PreviewKeyUpEvent });
            Flush(window);
            Require(!beforeHeights.SequenceEqual(left.CaptureLayout().Panels.Select(p => p.PreferredHeight)),
                "Keyboard resizing creates a saved height while untouched panels stay automatic.");
            Require(media.PreferredHeight is null, "Resizing Layers leaves Media in automatic sizing mode.");
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
            foreach (var id in new[] { "slideshow", "captions", "overlays" })
            {
                var panel = left.Panels.Single(p => p.PanelId == id);
                var tab = Tab(left, panel.Title + " panel");
                Toggle(tab); Flush(window);
                Require(panel.IsOpen && left.Panels.Single(p => p.PanelId == "photos").IsOpen,
                    panel.Title + " opens independently while Photos stays open.");
                Toggle(tab); Flush(window);
            }
            var hint = (Border)designer.FindName("PhotoEmptyState");
            var hintBounds = hint.TransformToAncestor(designer).TransformBounds(new Rect(hint.RenderSize));
            Require(hint.IsVisible && Math.Abs(hintBounds.Width - hint.ActualWidth) < .5,
                "Empty-state guidance retains native text size instead of shrinking with the physical canvas.");
            CheckFits(designer); Capture(window, captures, "Workspace-hybrid.png");
            vm.Document.Mode = ScreenMode.InfoScreen; Flush(window);
            var saved = left.CaptureLayout();
            var restored = new SettingsController(new AppSettingsService(path));
            Require(restored.WorkspaceLayout.Left.Panels.Select(p => (p.Id, p.IsOpen, p.PreferredHeight)).SequenceEqual(saved.Panels.Select(p => (p.Id, p.IsOpen, p.PreferredHeight))) &&
                restored.WorkspaceLayout.Right.Width == 390 && restored.IsCanvasFit,
                "Saved preferences restore open states, order, heights, widths and Fit.");
            using (var secondVm = new MainViewModel(restored, new SessionStateService(Path.Combine(root, "workspace-layout-second"))))
            {
                var secondWindow = new PCInfoScreenStudio.MainWindow(secondVm);
                try
                {
                    var secondLeft = (WorkspaceDock)secondWindow.FindName("LeftDock");
                    Require(secondLeft.CaptureLayout().Panels.Select(p => (p.Id, p.IsOpen, p.PreferredHeight)).SequenceEqual(saved.Panels.Select(p => (p.Id, p.IsOpen, p.PreferredHeight))),
                        "A new window actually restores the saved arrangement.");
                }
                finally { secondWindow.MarkSessionSavedForExit(); secondWindow.Close(); }
            }
            var beforeReset = (original.X, original.Y, original.Width, original.Height);
            Invoke(All<Button>(canvasArea).Single(b => Equals(b.Content, "Reset workspace"))); Flush(window);
            Require(vm.IsCanvasFit && left.PreferredPanelWidth == 300 && right.PreferredPanelWidth == 330 &&
                left.CaptureLayout().Panels.All(p => p.PreferredHeight is null) &&
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

    private static void CheckContentSizing(Window window, MainViewModel vm, WorkspaceDock left, WorkspaceDock right, string? captures)
    {
        var leftLayout = left.CaptureLayout(); var rightLayout = right.CaptureLayout();
        var mode = vm.Document.Mode; var selection = vm.SelectedWidget;
        try
        {
            foreach (var current in new[] { ScreenMode.InfoScreen, ScreenMode.PhotoFrame, ScreenMode.Hybrid })
            {
                vm.Document.Mode = current; vm.SelectedWidget = null;
                foreach (var panel in left.Panels.Concat(right.Panels)) panel.IsOpen = false;
                var active = left.Panels.Single(p => p.PanelId == (current switch
                    { ScreenMode.InfoScreen => "widgets", ScreenMode.Hybrid => "overlays", _ => "photos" }));
                active.IsOpen = true;
                var properties = right.Panels.Single(p => p.PanelId == "properties");
                properties.IsOpen = true; Flush(window);
                if (current != ScreenMode.PhotoFrame)
                {
                    Require(PanelHost(active).ActualHeight < 260 && PanelHost(active).ActualHeight < left.ActualHeight - 60,
                        current + " opens its small Widgets panel at content height, leaving unused space below.");
                    Require(PanelHost(properties).ActualHeight < 180,
                        current + " keeps an empty Properties panel compact on the right.");
                }
                if (current != ScreenMode.InfoScreen)
                {
                    var tab = Tab(left, "Add photos panel");
                    var label = All<TextBlock>(tab).Single(t => t.Text == "Add photos");
                    var bounds = label.TransformToAncestor(tab).TransformBounds(new Rect(label.RenderSize));
                    Require(bounds.Right <= tab.ActualWidth - 3, current + " keeps the complete Add photos label inside its narrow rail.");
                }
                Capture(window, captures, "Workspace-auto-" + current + ".png");
            }
        }
        finally
        {
            vm.Document.Mode = mode; vm.SelectedWidget = selection;
            left.RestoreLayout(leftLayout); right.RestoreLayout(rightLayout); Flush(window);
        }
    }

    private static void CheckDynamicContent(Action<Func<bool>> pump)
    {
        var section = new Expander { Header = "More settings", Content = new Border { Height = 180 } };
        var content = new StackPanel();
        content.Children.Add(new Button { Content = "Example setting" }); content.Children.Add(section);
        var first = new WorkspacePanel { PanelId = "first", Title = "Settings", Content = content };
        var second = new WorkspacePanel { PanelId = "second", Title = "More", Content = new Button { Content = "Another setting" } };
        var dock = new WorkspaceDock(); dock.Panels.Add(first); dock.Panels.Add(second);
        var window = new Window { Width = 430, Height = 650, Content = dock };
        try
        {
            window.Show(); pump(() => window.IsVisible); Flush(window);
            var compact = PanelHost(first).ActualHeight;
            Require(compact < 200 && PanelHost(second).ActualHeight < 130,
                "Several short panels use content heights instead of dividing all available height.");
            var splitter = All<GridSplitter>(dock).Single();
            splitter.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            splitter.RaiseEvent(new DragDeltaEventArgs(0, 20) { RoutedEvent = Thumb.DragDeltaEvent });
            splitter.RaiseEvent(new DragCompletedEventArgs(0, 20, true) { RoutedEvent = Thumb.DragCompletedEvent });
            Flush(window);
            Require(first.PreferredHeight is null && Math.Abs(PanelHost(first).ActualHeight - compact) < 1,
                "Cancelling a divider drag returns the panel to its automatic content height.");
            second.IsOpen = false; Flush(window);
            Require(Math.Abs(PanelHost(first).ActualHeight - compact) < 1,
                "Closing a neighbour does not stretch the remaining short panel.");
            section.IsExpanded = true; Flush(window);
            Require(PanelHost(first).ActualHeight > compact + 150,
                "Expanding content grows an automatic panel without reopening it.");
            section.IsExpanded = false; Flush(window);
            Require(Math.Abs(PanelHost(first).ActualHeight - compact) < 1,
                "Collapsing content shrinks an automatic panel again.");
            content.Children.Add(new Border { Height = 2000 }); second.IsOpen = true; Flush(window);
            var scroll = All<ScrollViewer>(PanelHost(first)).Single();
            Require(PanelHost(first).ActualHeight <= dock.ActualHeight / 2 + 1 && scroll.ScrollableHeight > 1000 && scroll.ViewportHeight > 0,
                "Long panels are bounded and scroll while a neighbouring panel stays visible.");
            Require(PanelHost(second).TransformToAncestor(dock).TransformBounds(new Rect(PanelHost(second).RenderSize)).Bottom <= dock.ActualHeight + 1,
                "The next panel remains inside the viewport beside long content.");
            var legacy = dock.CaptureLayout(); legacy.Panels[0].Weight = 10;
            content.Children.RemoveAt(content.Children.Count - 1); dock.RestoreLayout(legacy); Flush(window);
            Require(Math.Abs(PanelHost(first).ActualHeight - compact) < 1,
                "Legacy proportional weights do not force blank space into automatic panels.");
        }
        finally { window.Close(); }
    }

    private static Border PanelHost(WorkspacePanel panel)
    {
        for (DependencyObject? current = panel; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is Border { Child: Grid grid } border && grid.Children.OfType<DockPanel>().Any()) return border;
        throw new InvalidOperationException("Open panel host not found.");
    }

    private static void CheckPreferenceRepair(string root)
    {
        var path = Path.Combine(root, "workspace-layout-repair.json");
        File.WriteAllText(path, """{"SettingsVersion":5,"WeatherCity":"Keep me","UsbBrightness":72,"WorkspaceLayout":{"Left":{"Width":-500,"Panels":[null,{"Id":"layers","Weight":0,"PreferredHeight":-200},{"Id":"layers","Weight":999}]},"Right":null}}""");
        var loaded = new AppSettingsService(path).Load();
        Require(loaded.WeatherCity == "Keep me" && loaded.UsbBrightness == 72 && loaded.WorkspaceLayout.Left.Width == 240 &&
            loaded.WorkspaceLayout.Left.Panels.Count == 1 && loaded.WorkspaceLayout.Left.Panels[0].Weight == .1 &&
            loaded.WorkspaceLayout.Left.Panels[0].PreferredHeight is null && loaded.WorkspaceLayout.Right.Width == 330,
            "Malformed layout fields are repaired without losing unrelated valid preferences.");
    }

    private static void CheckFits(DesignerSurface surface)
    {
        var frame = (Border)surface.FindName("CanvasFrame");
        var bounds = frame.TransformToAncestor(surface).TransformBounds(new Rect(frame.RenderSize));
        Require(bounds.Left >= -.5 && bounds.Top >= -.5 && bounds.Right <= surface.ActualWidth + .5 && bounds.Bottom <= surface.ActualHeight + .5,
            $"Fit must keep the complete physical canvas inside its viewport: {bounds}, viewport {surface.RenderSize}.");
    }
    private static ToggleButton Tab(WorkspaceDock dock, string name) => All<ToggleButton>(dock).Single(t => t.IsVisible && AutomationProperties.GetName(t) == name);
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
