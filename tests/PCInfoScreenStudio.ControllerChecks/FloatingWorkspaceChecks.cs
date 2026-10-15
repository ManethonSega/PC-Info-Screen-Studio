using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PCInfoScreenStudio.Controls;
using PCInfoScreenStudio.Controllers;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;
using PCInfoScreenStudio.ViewModels;

internal static class FloatingWorkspaceChecks
{
    internal static void Check(Window owner, MainViewModel vm, WorkspaceDock left, WorkspaceDock right,
        string root, Action<Func<bool>> pump, string? captures)
    {
        var docking = left.Docking!;
        var original = new EditorWorkspaceLayout { Left = left.CaptureLayout(), Right = right.CaptureLayout() };
        var mode = vm.Document.Mode;
        var dirty = vm.IsDirty;
        var widget = vm.SelectedWidget!;
        var geometry = (widget.X, widget.Y, widget.Width, widget.Height);
        var widgets = left.Panels.Single(p => p.PanelId == "widgets");
        var photos = left.Panels.Single(p => p.PanelId == "photos");
        var liveContent = widgets.Content;
        try
        {
            owner.Width = 1440; owner.Height = 900;
            vm.Document.Mode = ScreenMode.InfoScreen; vm.IsCanvasFit = true;
            foreach (var p in left.Panels.Concat(right.Panels)) p.IsOpen = false;
            widgets.IsOpen = true; Flush(owner);
            var canvas = (Grid)owner.FindName("CanvasArea"); var before = canvas.ActualWidth;
            Menu(left.Host(widgets), "Widgets panel options", "Float panel"); Flush(owner);
            var floating = docking.FloatingWindows.Single();
            Require(widgets.IsFloating && floating.IsVisible && ReferenceEquals(floating.Owner, owner) && !floating.ShowInTaskbar,
                "The accessible Float menu opens a native owned tool window.");
            Require(ReferenceEquals(widgets.Content, liveContent) && ReferenceEquals(widgets.DataContext, vm),
                "Floating retains the live controls and view model.");
            Require(canvas.ActualWidth > before + 200, "Floating the only open left panel releases its canvas space.");
            Capture(owner, captures, "Workspace-floating-owner.png"); Capture(floating, captures, "Workspace-floating-widgets.png");
            floating.Width = 390; floating.Height = 260; floating.UpdateLayout();
            floating.PlaceOnScreen(new Point(-90000, -90000)); Flush(owner);
            var bounds = widgets.FloatingBounds!;
            var area = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)bounds.ScreenX, (int)bounds.ScreenY)).WorkingArea;
            Require(bounds.ScreenX >= area.Left && bounds.ScreenY >= area.Top && bounds.ScreenX < area.Right && bounds.ScreenY < area.Bottom,
                "Missing-monitor coordinates recover to a reachable working area.");

            // Same state machine used by captured header mouse events: preview, cancellation, cross-dock drop and outside drop.
            var header = Header(left.Host(widgets));
            var target = right.PointToScreen(new Point(20, 30));
            header.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
            Require(header.IsMouseCaptured, "The header mouse-down handler captures input for dragging across windows.");
            docking.UpdateDrag(target); Flush(owner);
            Require(docking.IsDragging && All<TextBlock>(right).Any(t => t.IsVisible && t.Text == "Dock right"),
                $"A drag exposes a visible target on an otherwise empty dock. Active={docking.IsDragging}; capture={header.IsMouseCaptured}; right={right.IsVisible}/{right.RenderSize}; labels={string.Join(",", All<TextBlock>(right).Where(t => t.Text.StartsWith("Dock ")).Select(t => t.Text + ":" + t.IsVisible))}");
            Capture(owner, captures, "Workspace-drag-target.png");
            header.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(floating), 0, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent }); Flush(owner);
            Require(widgets.IsFloating && floating.IsVisible && !docking.IsDragging && !header.IsMouseCaptured, "Cancelling a drag preserves the floating arrangement.");
            docking.BeginDrag(widgets, header, new Point()); docking.UpdateDrag(target); Flush(owner);
            target = right.PointToScreen(new Point(20, 30));
            docking.CompleteDrag(target, false); Flush(owner);
            Require(right.Panels.Contains(widgets) && !left.Panels.Contains(widgets) && !widgets.IsFloating && !floating.IsVisible,
                "Dropping into the opposite target moves the existing panel and closes its old tool window.");
            Require(ReferenceEquals(widgets.Content, liveContent), "Cross-docking never recreates widget controls.");
            Capture(owner, captures, "Workspace-cross-docked.png");
            var header2 = Header(right.Host(widgets));
            var outside = owner.PointToScreen(new Point(owner.ActualWidth / 2, 180));
            docking.BeginDrag(widgets, header2, right.PointToScreen(new Point(20, 30)));
            docking.UpdateDrag(outside); docking.CompleteDrag(outside, false); Flush(owner);
            Require(widgets.IsFloating && docking.FloatingWindows.Single().IsVisible, "Dropping outside either dock creates a floating window.");
            floating = docking.FloatingWindows.Single();
            floating.Close(); Flush(owner);
            Require(!widgets.IsOpen && widgets.IsFloating && !floating.IsVisible, "Closing a tool window collapses it without destroying its placement.");
            widgets.IsOpen = true; Flush(owner);
            Require(floating.IsVisible, "Reopening the rail entry restores its floating window.");
            vm.ToggleEditorModeCommand.Execute(null); Flush(owner);
            Require(!floating.IsVisible, "Live view hides floating panels.");
            vm.ToggleEditorModeCommand.Execute(null); Flush(owner);
            Require(floating.IsVisible, "Returning to Edit restores floating panels.");
            vm.Document.Mode = ScreenMode.PhotoFrame; Flush(owner);
            Require(!floating.IsVisible, "Mode-specific floating Widgets stay hidden in Photo Frame.");
            // Photo panels may move to the right even in Photo Frame; inspector panels stay mode-specific.
            docking.Dock(photos, true); Flush(owner);
            Require(right.ActualWidth > 100 && photos.IsVisible, "Photo Frame can dock + Photos on the right.");
            Menu(right.Host(photos), "+ Photos panel options", "Float panel"); Flush(owner);
            var photoWindow = docking.FloatingWindows.Single(w => w.Title == "+ Photos");
            Require(photoWindow.IsVisible && All<ScrollViewer>(photos).Count() == 1,
                "Floating + Photos retains a single internal playlist scroller.");
            Capture(photoWindow, captures, "Workspace-floating-photos.png");
            vm.Document.Mode = ScreenMode.Hybrid; Flush(owner);
            Require(photoWindow.IsVisible && !floating.IsVisible, "Hybrid keeps Photos while hiding Info-only Widgets.");
            vm.Document.Mode = ScreenMode.InfoScreen; Flush(owner);
            Require(!photoWindow.IsVisible && floating.IsVisible, "Mode changes restore the matching tools independently.");
            owner.Hide(); Flush(owner); Require(!floating.IsVisible, "Tray-style owner hiding hides all tool windows.");
            owner.Show(); Flush(owner); Require(floating.IsVisible, "Restoring the owner restores applicable tools.");
            owner.WindowState = WindowState.Minimized; Flush(owner);
            Require(!floating.IsVisible, "Minimizing the owner hides floating tools.");
            owner.WindowState = WindowState.Normal; Flush(owner);
            Require(floating.IsVisible, "Restoring from minimized restores floating tools.");

            var properties = left.Panels.Concat(right.Panels).Single(p => p.PanelId == "properties");
            docking.Float(properties); Flush(owner);
            var propertyWindow = docking.FloatingWindows.Single(w => w.Title == "Properties");
            Require(ReferenceEquals(propertyWindow.DataContext, vm) && propertyWindow.InputBindings.Count == owner.InputBindings.Count,
                "Floating Properties retains ancestor-window view model bindings and editor key bindings.");
            Capture(propertyWindow, captures, "Workspace-floating-properties.png");
            docking.Dock(properties, true); Flush(owner);

            var persisted = vm.WorkspaceLayout.ValidatedCopy();
            var path = Path.Combine(root, "floating-layout.json");
            var settings = new SettingsController(new AppSettingsService(path))
                { FirstRunCompleted = true, CloseToTray = false, AutoStartDisplay = false, StartWithWindows = false };
            settings.WorkspaceLayout = persisted;
            using (var secondVm = new MainViewModel(new SettingsController(new AppSettingsService(path)),
                new SessionStateService(Path.Combine(root, "floating-session"))))
            {
                var second = new PCInfoScreenStudio.MainWindow(secondVm);
                try
                {
                    second.Show(); pump(() => second.IsEnabled); secondVm.SetEditorActive(false); Flush(second);
                    var secondRight = (WorkspaceDock)second.FindName("RightDock");
                    var restored = secondRight.Panels.Single(p => p.PanelId == "widgets");
                    Require(restored.IsFloating && restored.IsOpen && restored.FloatingBounds is not null &&
                        secondRight.Docking!.FloatingWindows.Any(w => w.Title == "Widgets" && w.IsVisible),
                        "Restart restores cross-dock ownership, floating state, bounds and open state from actual settings.");
                    var owned = secondRight.Docking!.FloatingWindows.ToArray();
                    second.MarkSessionSavedForExit(); second.Close(); Flush(owner);
                    Require(owned.All(w => !w.IsVisible), "Owner exit closes every floating HWND.");
                }
                finally { second.MarkSessionSavedForExit(); second.Close(); }
            }
            Require(vm.IsDirty == dirty && ReferenceEquals(vm.SelectedWidget, widget) &&
                (widget.X, widget.Y, widget.Width, widget.Height) == geometry,
                "All docking, floating and recovery actions preserve selection, artwork and dirty state.");
            var defaults = docking.DefaultLayout(); docking.Restore(defaults); Flush(owner);
            var layers = left.Panels.Single(p => p.PanelId == "layers");
            var below = left.PointToScreen(new Point(150, left.ActualHeight - 20));
            docking.BeginDrag(widgets, Header(left.Host(widgets)), left.PointToScreen(new Point(150, 20)));
            docking.UpdateDrag(below); docking.CompleteDrag(below, false); Flush(owner);
            var order = left.CaptureLayout().Panels.Select(p => p.Id).ToList();
            Require(order.IndexOf("widgets") > order.IndexOf("layers") && widgets.IsOpen && layers.IsOpen,
                "Dropping below another open panel changes stack order while preserving independent open states.");
            Capture(owner, captures, "Workspace-drag-reordered.png");
            docking.Restore(defaults); Flush(owner);
            Require(docking.FloatingWindows.Count == 0 && left.Panels.Contains(widgets) && left.Panels.Contains(photos) &&
                !widgets.IsFloating && widgets.PreferredHeight is null, "Workspace reset restores original sides and closes floating windows.");
            var corrupt = new EditorWorkspaceLayout
            {
                Left = new() { Panels = [new() { Id = "widgets", IsFloating = true,
                    FloatingBounds = new() { ScreenX = double.NaN, Width = -1 } }] },
                Right = new() { Panels = [new() { Id = "widgets" }] }
            }.ValidatedCopy();
            Require(corrupt.Left.Panels.Single().FloatingBounds is null && corrupt.Right.Panels.Count == 0,
                "Invalid bounds and duplicate cross-dock IDs are repaired without discarding panel state.");
            Console.WriteLine("PASS: native floating menus, live control preservation, cross-docking, drag previews/cancel/outside drop, mode/Live/tray/minimize lifecycle, monitor recovery, settings restart and reset.");
        }
        finally { vm.Document.Mode = mode; docking.Restore(original); Flush(owner); }
    }

    private static DockPanel Header(Border host) => ((Grid)host.Child).Children.OfType<DockPanel>().Single();
    private static void Menu(Border host, string name, string action)
    {
        var button = All<Button>(host).Single(b => AutomationProperties.GetName(b) == name);
        ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)!).Invoke();
        Flush(Window.GetWindow(button)!);
        var menu = button.ContextMenu!;
        ((MenuItem)menu.Items.Cast<MenuItem>().Single(i => Equals(i.Header, action))).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        menu.IsOpen = false;
    }
    private static void Flush(Window window)
    { Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.ContextIdle); window.UpdateLayout(); }
    private static IEnumerable<T> All<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in All<T>(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    private static void Capture(Window window, string? directory, string name)
    {
        if (directory is null) return;
        Directory.CreateDirectory(directory); var element = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element); using var file = File.Create(Path.Combine(directory, name));
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(file);
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
