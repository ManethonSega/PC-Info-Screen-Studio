using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using PCInfoScreenStudio.Controllers;
using PCInfoScreenStudio.Controls;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;
using PCInfoScreenStudio.ViewModels;

internal static class UiPolishChecks
{
    public static void Check(string root, Action<Func<bool>> pump, string? captureDirectory = null)
    {
        var settings = new SettingsController(new AppSettingsService(System.IO.Path.Combine(root, "ui-polish.json")))
        { FirstRunCompleted = true, StartWithWindows = false, AutoStartDisplay = false, CloseToTray = false };
        var vm = new MainViewModel(settings, new SessionStateService(System.IO.Path.Combine(root, "ui-polish-session")));
        vm.SetEditorActive(false);
        var window = new PCInfoScreenStudio.MainWindow(vm);
        try
        {
            window.Show();
            pump(() => window.IsEnabled);
            vm.SetEditorActive(false);
            var surface = All<DesignerSurface>(window).Single();
            var photoHint = (Border)surface.FindName("PhotoEmptyState");
            var hybridHint = (Border)surface.FindName("HybridEmptyState");
            var properties = (ContentControl)window.FindName("WidgetProperties");

            vm.Document.Mode = ScreenMode.PhotoFrame;
            vm.Document.PhotoFrame.Photos.Clear();
            Flush();
            Assert(photoHint.IsVisible && !hybridHint.IsVisible, "An empty Photo Frame must explain how to add photos.");
            vm.Document.PhotoFrame.Photos.Add(new PhotoFrameItem { SourcePath = "not-loaded.png" });
            Flush();
            Assert(!photoHint.IsVisible, "Adding a photo must hide the empty-photo prompt.");
            vm.Document.PhotoFrame.Photos.Clear();
            vm.Document.Mode = ScreenMode.Hybrid;
            vm.Document.HybridWidgets.Clear();
            vm.SelectedWidget = null;
            Flush();
            Assert(photoHint.IsVisible && hybridHint.IsVisible && !properties.IsVisible,
                "Empty Hybrid must show useful prompts and hide all blank property editors.");
            vm.AddWidgetCommand.Execute("Value");
            Flush();
            Assert(!hybridHint.IsVisible && properties.IsVisible, "Adding an overlay must reveal its properties and remove its prompt.");
            vm.SelectedWidget = null;
            Flush();
            Assert(!properties.IsVisible && !hybridHint.IsVisible, "Deselecting an existing overlay must hide fields without claiming no overlays exist.");
            vm.Document.HybridWidgets.Clear();
            vm.ToggleEditorModeCommand.Execute(null);
            Flush();
            Assert(!photoHint.IsVisible && !hybridHint.IsVisible, "Live view must not contain editor-only empty-state hints.");
            vm.ToggleEditorModeCommand.Execute(null);
            vm.Document.Mode = ScreenMode.InfoScreen;
            vm.SelectedWidget = null;
            Flush();
            Assert(!photoHint.IsVisible && !hybridHint.IsVisible && !properties.IsVisible, "Info Screen must not show photo prompts or blank property fields.");
            vm.AddWidgetCommand.Execute("CircularGauge");
            Flush();
            CheckAlignment(window, vm);
            CheckNumbers(window, vm);
            vm.Document.Mode = ScreenMode.PhotoFrame;
            Flush();
            CheckPhotoNumbers(window, vm);
            vm.Document.Mode = ScreenMode.Hybrid;
            vm.AddWidgetCommand.Execute("Value");
            Flush();
            var hybrid = vm.SelectedWidget!;
            CheckField(Field(window, "Width"), "7", "20", () => hybrid.Width, 20);
            if (captureDirectory is not null)
            {
                Draft(Field(window, "Width"), "2");
                Draft(Field(window, "Opacity"), "2");
                Draft(Field(window, "Minimum"), "100");
                Capture((FrameworkElement)window.Content, captureDirectory, "Numeric-validation.png");
            }
            var before = vm.Document.Widgets.Select(w => w.Width).ToArray();
            Assert(before.Length > 0 && vm.Document.HybridWidgets.Count == 1, "Hybrid validation must retain independent Info Screen layers.");
            Console.WriteLine("PASS: actual WPF empty states, contextual properties, six alignment icons, draft validation, range relationships, photo settings and Hybrid.");
        }
        finally { window.MarkSessionSavedForExit(); window.Close(); }
    }

    private static void CheckAlignment(PCInfoScreenStudio.MainWindow window, MainViewModel vm)
    {
        var widget = vm.SelectedWidget!;
        foreach (var direction in new[] { "Left", "Center", "Right", "Top", "Middle", "Bottom" })
        {
            var button = (Button)window.FindName("Align" + direction + "Button");
            Assert(button.Content is System.Windows.Shapes.Path && !string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)) &&
                button.ToolTip is string help && help.Contains("multiple widgets"), "Alignment buttons need vector icons, accessible names and descriptive tooltips.");
            widget.X = 37; widget.Y = 43;
            button.Command!.Execute(button.CommandParameter);
            Assert(direction switch
            {
                "Left" => widget.X == 0,
                "Center" => widget.X == (vm.Document.CanvasWidth - widget.Width) / 2,
                "Right" => widget.X == vm.Document.CanvasWidth - widget.Width,
                "Top" => widget.Y == 0,
                "Middle" => widget.Y == (vm.Document.CanvasHeight - widget.Height) / 2,
                _ => widget.Y == vm.Document.CanvasHeight - widget.Height
            }, "Alignment icon must retain its corresponding canvas action: " + direction);
        }
        var second = new WidgetModel { X = 121, Y = 82, Width = 40, Height = 40 };
        vm.Document.Widgets.Add(second);
        widget.X = 25; widget.Y = 30;
        vm.SelectWidgets(new[] { widget, second }, false);
        var right = Math.Max(widget.X + widget.Width, second.X + second.Width);
        var align = (Button)window.FindName("AlignRightButton");
        align.Command!.Execute(align.CommandParameter);
        Assert(widget.X + widget.Width == right && second.X + second.Width == right, "Multi-selection alignment must retain selection-bounds behaviour.");
        vm.SelectedWidget = widget;
        Flush();
    }

    private static void CheckNumbers(PCInfoScreenStudio.MainWindow window, MainViewModel vm)
    {
        var widget = vm.SelectedWidget!;
        CheckField(Field(window, "Width"), "2", "20", () => widget.Width, 20);
        CheckField(Field(window, "Height"), "0", "48", () => widget.Height, 48);
        CheckField(Field(window, "X"), "-1", "12", () => widget.X, 12);
        CheckField(Field(window, "Opacity"), "1.1", "0.5", () => widget.Opacity, .5);
        CheckField(Field(window, "BackgroundTransparency"), "101", "40", () => widget.BackgroundTransparency, 40);
        CheckField(Field(window, "FontSize"), "5", "24", () => widget.FontSize, 24);
        var min = Field(window, "Minimum");
        var max = Field(window, "Maximum");
        CheckField(min, "100", "-20", () => widget.Minimum, -20);
        CheckField(max, "-20", "120", () => widget.Maximum, 120);
        Draft(min, "130");
        Assert(Validation.GetHasError(min), "A proposed minimum above Max must be invalid.");
        widget.Maximum = 140;
        Flush();
        Assert(!Validation.GetHasError(min), "A pending range error must clear when the other endpoint makes it valid.");
        min.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Assert(widget.Minimum == 130, "Corrected range draft must commit without silently clamping it.");
        var width = Field(window, "Width");
        foreach (var value in new[] { "", "abc", "NaN", "Infinity", "1e999" })
        {
            var previous = widget.Width;
            Draft(width, value);
            width.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert(Validation.GetHasError(width) && widget.Width == previous, "Malformed/non-finite input must not reach the model: " + value);
        }
        Draft(width, "20"); width.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        // Binding culture, rather than hardcoded dot parsing, must accept comma decimals.
        var opacity = Field(window, "Opacity");
        opacity.Language = XmlLanguage.GetLanguage("de-DE");
        Draft(opacity, "0,75");
        opacity.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Assert(!Validation.GetHasError(opacity) && widget.Opacity == .75, "Numeric validation must honour the field's binding culture.");
        opacity.Language = XmlLanguage.GetLanguage("en-US");
        vm.AddWidgetCommand.Execute("Graph");
        Flush();
        var graph = vm.SelectedWidget!;
        CheckField(Field(window, "HistorySeconds"), "2.5", "20", () => graph.HistorySeconds, 20);
        CheckField(Field(window, "LineThickness"), "0", "2", () => graph.LineThickness, 2);
        vm.SelectedWidget = widget;
        Flush();
        Assert(!Validation.GetHasError(Field(window, "Width")) && Field(window, "Width").Text == "20",
            "Changing widgets must not leak a previous field's draft or errors.");
    }

    private static void CheckPhotoNumbers(PCInfoScreenStudio.MainWindow window, MainViewModel vm)
    {
        var photos = vm.Document.PhotoFrame;
        CheckField(Field(window, "Document.PhotoFrame.DefaultDurationSeconds"), "0", "10", () => photos.DefaultDurationSeconds, 10);
        CheckField(Field(window, "Document.PhotoFrame.TransitionDurationSeconds"), "11", "0", () => photos.TransitionDurationSeconds, 0);
        CheckField(Field(window, "Document.PhotoFrame.CaptionFontSize"), "101", "22", () => photos.CaptionFontSize, 22);
        CheckField(Field(window, "Document.PhotoFrame.CaptionOutlineThickness"), "-1", "0", () => photos.CaptionOutlineThickness, 0);
    }

    private static void CheckField(TextBox box, string invalid, string valid, Func<double> value, double expected)
    {
        var original = value();
        Draft(box, invalid);
        Assert(Validation.GetHasError(box), "An invalid draft must show an error before leaving the field: " + invalid);
        box.ApplyTemplate();
        var label = (TextBlock)box.Template.FindName("InlineValidationError", box);
        Assert(label.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(label.Text), "Numeric error explanation must appear beside the field.");
        box.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Assert(value() == original, "Invalid drafts must preserve the last valid model value.");
        Draft(box, valid);
        Assert(!Validation.GetHasError(box), "A corrected draft must clear the error.");
        Assert(value() == original, "Draft validation must not commit while the user is typing.");
        box.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Flush();
        Assert(value() == expected && !Validation.GetHasError(box), "Valid input must commit exactly as entered: " + valid);
    }

    private static void Capture(FrameworkElement root, string directory, string name)
    {
        Directory.CreateDirectory(directory);
        // The runner's desktop can be smaller than the app's normal editor size.
        // Capture the full control tree rather than a clipped monitor viewport.
        root.Measure(new Size(1560, 1040));
        root.Arrange(new Rect(0, 0, 1560, 1040));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1560, 1040, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(System.IO.Path.Combine(directory, name));
        encoder.Save(output);
    }

    private static void Draft(TextBox box, string text) { box.SetCurrentValue(TextBox.TextProperty, text); Flush(); }
    private static TextBox Field(DependencyObject root, string path) => All<TextBox>(root).First(box =>
        box.IsVisible && BindingOperations.GetBinding(box, TextBox.TextProperty)?.Path.Path == path);
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in All<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.ContextIdle);
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
