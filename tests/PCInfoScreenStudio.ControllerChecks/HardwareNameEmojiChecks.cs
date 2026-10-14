using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using PCInfoScreenStudio.Controllers;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Rendering;
using PCInfoScreenStudio.Services;
using PCInfoScreenStudio.ViewModels;
using SkiaSharp;

internal static class HardwareNameEmojiChecks
{
    public static async Task RunAsync(string root, Action<Func<bool>> pump, string? captures)
    {
        var modules = new (string?, string?)[] { (" G.Skill ", " F5-5200J3636D32G "), ("G.Skill", "F5-5200J3636D32G") };
        Assert(HardwareInventoryService.FormatMemoryNames(modules) == "G.Skill F5-5200J3636D32G", "RAM names must retain the reported brand and model, trimming and deduplicating identical modules.");
        Assert(HardwareInventoryService.FormatMemoryNames([(null, "M378A"), ("Unknown", "Default string")]) == "M378A", "Missing brands must not be invented; firmware placeholders must be suppressed.");
        var widget = new WidgetModel { Type = WidgetType.HardwareName, DataSource = "Hardware.GPUName", RuntimeText = "AMD Radeon RX 7800 XT" };
        Assert(widget.HardwareNameText == "AMD Radeon RX 7800 XT", "Automatic text must show the current machine's detected model.");
        widget.HardwareNameText = "My Radeon 👌💕👏";
        widget.RuntimeText = "Updated detected name";
        Assert(!widget.UseDetectedHardwareName && widget.HardwareNameText == "My Radeon 👌💕👏", "Metric refreshes must not overwrite the user's edited name.");
        Assert(widget.Clone().CustomHardwareName == widget.CustomHardwareName && !widget.Clone().UseDetectedHardwareName, "Duplicating must retain the custom-name choice.");
        widget.UseDetectedHardwareName = true;
        Assert(widget.HardwareNameText == "Updated detected name", "Returning to automatic must use the current detected value.");
        widget.UseDetectedHardwareName = false;
        var settings = new SettingsController(new AppSettingsService(Path.Combine(root, "hardware-name-settings.json")))
        { FirstRunCompleted = true, StartWithWindows = false, AutoStartDisplay = false, CloseToTray = false };
        var vm = new MainViewModel(settings, new SessionStateService(Path.Combine(root, "hardware-name-session")));
        vm.SetEditorActive(false);
        var window = new PCInfoScreenStudio.MainWindow(vm);
        try
        {
            window.Show(); pump(() => window.IsEnabled); vm.SetEditorActive(false);
            foreach (var mode in new[] { ScreenMode.InfoScreen, ScreenMode.Hybrid })
            {
                vm.Document.Mode = mode;
                vm.AddWidgetCommand.Execute("HardwareName");
                var added = vm.SelectedWidget!;
                Assert(added.Type == WidgetType.HardwareName && added.DataSource == "Hardware.CPUName", "Hardware name must be addable in Info Screen and Hybrid.");
                added.RuntimeText = "Intel Core i9-13900K";
                Flush(); window.UpdateLayout();
                var editor = Find<TextBox>(window, t => t.Name == "HardwareNameTextEditor") ?? throw new InvalidOperationException("Hardware name editor missing.");
                Assert(editor.Text == "Intel Core i9-13900K", "The actual property field must display the detected name.");
                editor.Text = "My CPU 💕";
                editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                added.RuntimeText = "Detected refresh";
                Flush();
                Assert(added.HardwareNameText == "My CPU 💕" && editor.Text == "My CPU 💕", "Editing the actual WPF field must switch to custom and survive refreshes.");
            }
        }
        finally { window.MarkSessionSavedForExit(); window.Close(); vm.Dispose(); }
        using var workspace = new ThemeWorkspace(new ThemeDocument(), Path.Combine(root, "hardware-name-workspace"));
        workspace.Document.Widgets.Add(widget);
        var package = new ThemePackageService();
        var path = Path.Combine(root, "hardware-name.t3theme");
        await package.SaveAsync(workspace, path);
        using var restored = await package.LoadAsync(path);
        Assert(restored.Document.Widgets.Single().CustomHardwareName == "My Radeon 👌💕👏" && !restored.Document.Widgets.Single().UseDetectedHardwareName,
            "Theme save/reopen must retain Unicode and the override, without storing runtime detection.");
        workspace.Document.HybridWidgets.Add(widget.Clone());
        var modes = new ModeThemeService(Path.Combine(root, "hardware-name-themes"));
        var preset = modes.Load(modes.Save(workspace.Document, ScreenMode.Hybrid, "Names"));
        Assert(preset.HybridWidgets.Single().CustomHardwareName == widget.CustomHardwareName, "Hybrid presets must retain editable names too.");
        CheckColourRendering(root, captures);
        Console.WriteLine("PASS: hardware brands, auto/custom editing, WPF bindings, cloning, package/Hybrid persistence and coloured emoji rendering.");
    }

    private static void CheckColourRendering(string root, string? captures)
    {
        using var face = SKTypeface.FromFamilyName("Segoe UI");
        using var font = new SKFont(face, 48);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        foreach (var text in new[] { "👌💕👏", "👌🏽 👏🏻", "👩‍💻", "❤️ 1️⃣" })
        {
            using var image = new SKBitmap(600, 100);
            using var canvas = new SKCanvas(image); canvas.Clear(SKColors.Black);
            ColoredTextRenderer.Draw(canvas, text, 8, 72, font, paint);
            Assert(CountColourPixels(image) > 100, "The actual Skia output must contain coloured glyph pixels for " + text + ", not monochrome glyphs or empty boxes.");
            Assert(ColoredTextRenderer.Measure(text, font, paint) > 20, "Measured emoji width must match the drawable fallback runs.");
        }
        Assert(ColoredTextRenderer.Measure("👩‍💻", font, paint) < ColoredTextRenderer.Measure("👩💻", font, paint), "A supported joined sequence must shape as one emoji, not separate characters.");
        using var workspace = new ThemeWorkspace(new ThemeDocument { CanvasWidth = 1280, CanvasHeight = 720, BackgroundColor = "#FF000000" }, Path.Combine(root, "emoji-render"));
        var renderer = new ThemeRenderer();
        var types = new[] { WidgetType.Text, WidgetType.HardwareName, WidgetType.Value, WidgetType.CircularGauge, WidgetType.BarGauge, WidgetType.Graph };
        for (var i = 0; i < types.Length; i++)
        {
            var w = new WidgetModel { Type = types[i], X = (i % 3) * 420 + 8, Y = (i / 3) * 260 + 8, Width = 410, Height = 220,
                Label = "Hardware 👌💕👏", CustomHardwareName = "GPU 👌💕👏", UseDetectedHardwareName = false, FontFamily = "Orbitron", FontSize = 48,
                ForegroundColor = "#FFFFFFFF", AccentColor = "#FFCCCCCC", SecondaryColor = "#FF333333" };
            workspace.Document.Widgets.Clear(); workspace.Document.Widgets.Add(w);
            using var image = renderer.Render(workspace);
            Assert(CountColourPixels(image) > 20, "Emoji fallback must work with a custom font in " + types[i] + ".");
        }
        workspace.Document.Widgets.Clear();
        for (var i = 0; i < types.Length; i++)
            workspace.Document.Widgets.Add(new WidgetModel { Type = types[i], X = (i % 3) * 420 + 8, Y = (i / 3) * 260 + 8, Width = 410, Height = 220,
                Label = types[i] + " 👌💕👏", CustomHardwareName = "Radeon RX 7800 XT 👌", UseDetectedHardwareName = false, FontSize = 32,
                ForegroundColor = "#FFFFFFFF", AccentColor = "#FF18D7F1", SecondaryColor = "#FF1A2436" });
        using var board = renderer.Render(workspace);
        if (captures is not null)
        {
            Directory.CreateDirectory(captures);
            using var data = board.Encode(SKEncodedImageFormat.Png, 100);
            using var output = File.Create(Path.Combine(captures, "Hardware-names-and-emojis.png")); data.SaveTo(output);
        }
        workspace.Document.Mode = ScreenMode.PhotoFrame;
        workspace.Document.RuntimeMode = RuntimeScreenMode.PhotoFrame;
        workspace.Document.PhotoFrame.Photos.Add(new PhotoFrameItem { SourcePath = Path.Combine(root, "emoji-photo.png") });
        using (var blank = new SKBitmap(1280, 720))
        {
            using var c = new SKCanvas(blank); c.Clear(SKColors.Black);
            using var data = blank.Encode(SKEncodedImageFormat.Png, 100);
            using var output = File.Create(workspace.Document.PhotoFrame.Photos[0].SourcePath!); data.SaveTo(output);
        }
        workspace.Document.PhotoFrame.CaptionMode = PhotoCaptionMode.Custom;
        workspace.Document.PhotoFrame.CustomCaption = "Photo 👌💕👏";
        workspace.Document.PhotoFrame.CaptionFontSize = 48;
        workspace.Document.PhotoFrame.CaptionColor = "#FFFFFFFF";
        workspace.Document.PhotoFrame.CaptionOutlineThickness = 2;
        using var caption = renderer.Render(workspace);
        Assert(CountColourPixels(caption) > 100, "Photo captions must preserve coloured emoji while outlining the ordinary text.");
    }

    private static int CountColourPixels(SKBitmap image)
        => image.Pixels.Count(c => c.Alpha > 150 && Math.Max(c.Red, Math.Max(c.Green, c.Blue)) - Math.Min(c.Red, Math.Min(c.Green, c.Blue)) > 35);
    private static void Flush() => System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), System.Windows.Threading.DispatcherPriority.ContextIdle);
    private static T? Find<T>(DependencyObject parent, Func<T, bool> predicate) where T : DependencyObject
    {
        if (parent is T match && predicate(match)) return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (Find(VisualTreeHelper.GetChild(parent, i), predicate) is { } result) return result;
        return null;
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
