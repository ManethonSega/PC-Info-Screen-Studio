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
            CheckBrightnessControl(window, vm, settings, pump, captureDirectory);
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
        CheckVideoControls(window, vm);
        vm.SelectedWidget = widget; Flush();
    }

    private static void CheckBrightnessControl(PCInfoScreenStudio.MainWindow window, MainViewModel vm,
        SettingsController settings, Action<Func<bool>> pump, string? captureDirectory)
    {
        var button = (Button)window.FindName("BrightnessButton");
        var popup = (System.Windows.Controls.Primitives.Popup)window.FindName("BrightnessPopup");
        var slider = (Slider)window.FindName("BrightnessSlider");
        var reset = (Button)window.FindName("ResetBrightnessButton");
        var before = System.Text.Json.JsonSerializer.Serialize(vm.Document);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        pump(() => popup.IsOpen && slider.IsVisible);
        Assert(slider.Value == 50 && slider.Minimum == 0 && slider.Maximum == 100 && slider.IsEnabled,
            "Top-bar brightness must expose an accessible 0..100 slider, including disconnected presets.");
        slider.SetCurrentValue(Slider.ValueProperty, 32d);
        pump(() => vm.DisplayBrightness == 32 && settings.SerialBrightness == 32);
        Assert(vm.BrightnessLabel == "Brightness 32%" && !slider.GetBindingExpression(Slider.ValueProperty)!.HasError,
            "Slider binding and toolbar percentage must update the selected family's saved level.");
        if (captureDirectory is not null) Capture((FrameworkElement)popup.Child, captureDirectory, "Brightness-control.png", naturalSize: true);
        slider.SetCurrentValue(Slider.ValueProperty, 41d);
        popup.IsOpen = false;
        Flush();
        Assert(settings.SerialBrightness == 41, "Closing the popup must commit a final slider edit without waiting for its binding delay.");
        Assert(System.Text.Json.JsonSerializer.Serialize(vm.Document) == before, "Brightness edits must not change theme content.");
        vm.CanvasSize = DisplayCanvasSize.Turing52;
        pump(() => !vm.IsDeviceBusy);
        Assert(vm.DisplayBrightness == 50 && vm.BrightnessLabel == "Brightness 50%", "Changing screen family must refresh the toolbar brightness.");
        var usbBefore = System.Text.Json.JsonSerializer.Serialize(vm.Document);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        pump(() => popup.IsOpen && slider.IsVisible);
        slider.SetCurrentValue(Slider.ValueProperty, 72d);
        pump(() => settings.UsbBrightness == 72);
        reset.Command.Execute(null);
        Flush();
        Assert(slider.Value == 50 && settings.UsbBrightness == 50 && settings.SerialBrightness == 41,
            "Reset must affect only the selected family and immediately refresh the slider.");
        var key = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(slider), 0, System.Windows.Input.Key.Escape)
            { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
        slider.RaiseEvent(key);
        Assert(!popup.IsOpen && key.Handled, "Escape must close brightness controls and return to the toolbar.");
        Assert(System.Text.Json.JsonSerializer.Serialize(vm.Document) == usbBefore, "USB brightness edits must also stay outside theme content.");
        vm.CanvasSize = DisplayCanvasSize.Serial35;
        pump(() => !vm.IsDeviceBusy);
        Assert(vm.DisplayBrightness == 41, "Returning to the serial family must restore its brightness.");
        var originalWidth = window.Width;
        window.Width = window.MinWidth;
        Flush();
        var position = button.TranslatePoint(new Point(0, 0), (UIElement)window.Content);
        Assert(position.X >= 0 && position.X + button.ActualWidth <= ((FrameworkElement)window.Content).ActualWidth,
            "Brightness must fit in the toolbar at the minimum supported window width.");
        window.Width = originalWidth;
        Flush();
    }

    private static void CheckVideoControls(PCInfoScreenStudio.MainWindow window, MainViewModel vm)
    {
        var originalSize = vm.CanvasSize;
        var video = new WidgetModel { Type = WidgetType.Video };
        vm.Document.Widgets.Add(video); vm.SelectedWidget = video;
        vm.CanvasSize = DisplayCanvasSize.Turing52; Flush();
        var fps = Field(window, "TargetFps");
        CheckField(fps, "22", "20", () => video.TargetFps, 20);
        Assert(!video.AutoOptimize, "Committing a manual video FPS must leave Auto mode");
        var automatic = All<CheckBox>(window).Single(box => box.IsVisible && BindingOperations.GetBinding(box, CheckBox.IsCheckedProperty)?.Path.Path == "AutoOptimize");
        automatic.SetCurrentValue(CheckBox.IsCheckedProperty, true); Flush();
        Assert(video.AutoOptimize && video.TargetFps == 0 && fps.Text == "0", "Selecting Auto must replace the previous manual FPS");
        vm.CanvasSize = DisplayCanvasSize.Serial35; Flush();
        CheckField(fps, "6", "5", () => video.TargetFps, 5);
        video.Type = WidgetType.AnimatedImage; Flush();
        fps = Field(window, "TargetFps");
        Draft(fps, "60"); fps.GetBindingExpression(TextBox.TextProperty)!.UpdateSource(); Flush();
        Assert(video.TargetFps == 60 && !Validation.GetHasError(fps), "GIF FPS keeps its own 60 fps limit");
        vm.Document.Widgets.Remove(video); vm.CanvasSize = originalSize; Flush();
        Console.WriteLine("PASS: WPF USB/serial video FPS validation, explicit Auto resets manual FPS, GIF FPS unaffected.");
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

    private static void Capture(FrameworkElement root, string directory, string name, bool naturalSize = false)
    {
        Directory.CreateDirectory(directory);
        // The runner's desktop can be smaller than the app's normal editor size.
        // Capture the full control tree rather than a clipped monitor viewport.
        if (!naturalSize)
        {
            root.Measure(new Size(1560, 1040));
            root.Arrange(new Rect(0, 0, 1560, 1040));
        }
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(naturalSize ? (int)Math.Ceiling(root.ActualWidth) : 1560,
            naturalSize ? (int)Math.Ceiling(root.ActualHeight) : 1040, 96, 96, PixelFormats.Pbgra32);
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
