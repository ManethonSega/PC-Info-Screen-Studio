using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using PCInfoScreenStudio.Controllers;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;
using PCInfoScreenStudio.ViewModels;
using SkiaSharp;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new PCInfoScreenStudio.App();
        app.InitializeComponent();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var root = Path.Combine(Path.GetTempPath(), "PCInfoScreenStudio-controller-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            CheckSettings(root);
            CheckEditor();
            CheckWidgetBackground(root);
            CheckPhotos(root);
            CheckDevice(root);
            CheckThemeModes(root);
            CheckThemeFolderReload(root);
            CheckDashboard();
            CheckPageNavigation(root);
            if (args.Length == 2 && args[0] == "--capture-ui") UiPreviews.Capture(args[1]);
            Console.WriteLine("PASS: preferences, editor, photos, device queue, mode themes, dashboard, and navigation checks.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            ThemeRendererCleanup();
            Directory.Delete(root, recursive: true);
        }
    }

    private static void CheckThemeModes(string root)
    {
        var service = new ModeThemeService(Path.Combine(root, "themes"));
        var document = new ThemeDocument { Mode = ScreenMode.PhotoFrame };
        var photo = new PhotoFrameItem { DisplayName = "My photo", SourcePath = Path.Combine(root, "linked-only.png") };
        document.PhotoFrame.Photos.Add(photo);
        document.PhotoFrame.CaptionFontSize = 24;
        document.PhotoFrame.CaptionFontFamily = "Orbitron";
        var photoPath = service.Save(document, ScreenMode.PhotoFrame, "Evening");
        Assert(photoPath.EndsWith(".pcphoto") && Path.GetDirectoryName(photoPath)!.EndsWith("Photo Frame Themes"), "Photo themes must use their own folder and extension.");
        var preset = service.Load(photoPath);
        Assert(preset.PhotoFrame.Photos.Count == 0 && !File.ReadAllText(photoPath).Contains("linked-only"), "Settings themes must never contain photos or playlist entries.");
        document.PhotoFrame.CaptionFontSize = 12;
        ModeThemeService.Apply(document, preset);
        Assert(document.PhotoFrame.Photos.Count == 1 && ReferenceEquals(document.PhotoFrame.Photos[0], photo), "Applying a theme must retain the original linked playlist.");
        Assert(document.PhotoFrame.CaptionFontSize == 24, "Applying a theme must update global caption settings.");
        Assert(document.PhotoFrame.CaptionFontFamily == "Orbitron", "Caption font choice must survive photo theme save/load.");
        document.Mode = ScreenMode.Hybrid;
        var overlay = new WidgetModel
        {
            Type = WidgetType.Text, Name = "Clock", Label = "Clock overlay", X = 117, Y = 63,
            Width = 185, Height = 47, Rotation = 12, ZIndex = 4, IsLocked = true, FontSize = 23,
            GroupId = Guid.NewGuid()
        };
        document.HybridWidgets.Add(overlay);
        var hybridPath = service.Save(document, ScreenMode.Hybrid, "Overlay");
        Assert(hybridPath.EndsWith(".pchybrid") && Path.GetDirectoryName(hybridPath)!.EndsWith("Hybrid Themes"), "Hybrid themes must use their own folder and extension.");
        var hybrid = service.Load(hybridPath);
        document.HybridWidgets.Clear();
        document.PhotoFrame.CaptionFontFamily = "Consolas";
        ModeThemeService.Apply(document, hybrid);
        Assert(document.HybridWidgets.Count == 1 && document.PhotoFrame.Photos.Count == 1, "Hybrid loading must restore overlays and retain photos.");
        Assert(document.PhotoFrame.CaptionFontFamily == "Orbitron", "Hybrid theme loading must restore the saved caption font.");
        var restored = document.HybridWidgets[0];
        Assert(restored.Id == overlay.Id && restored.Name == overlay.Name && restored.X == 117 && restored.Y == 63 &&
            restored.Width == 185 && restored.Height == 47 && restored.Rotation == 12 && restored.ZIndex == 4 &&
            restored.IsLocked && restored.GroupId == overlay.GroupId && restored.FontSize == 23,
            "Hybrid save/load must preserve overlay identity, position, layer order, grouping, locking and size exactly.");
        service.SaveToPath(document, ScreenMode.Hybrid, hybridPath);
        ModeThemeService.Apply(document, service.Load(hybridPath));
        Assert(document.HybridWidgets[0].X == 117 && document.HybridWidgets[0].Y == 63 && document.HybridWidgets[0].Name == "Clock",
            "Repeated Hybrid save/load must not offset or rename overlays.");
        var legacy = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(hybridPath))!.AsObject();
        legacy.Remove("FormatVersion");
        var legacyWidget = legacy["HybridWidgets"]![0]!;
        legacyWidget["X"] = 127;
        legacyWidget["Y"] = 73;
        legacyWidget["ZIndex"] = 5;
        legacyWidget["Name"] = "Clock copy";
        var legacyPath = Path.Combine(service.DirectoryFor(ScreenMode.Hybrid), "Legacy.pchybrid");
        File.WriteAllText(legacyPath, legacy.ToJsonString());
        ModeThemeService.Apply(document, service.Load(legacyPath));
        Assert(document.HybridWidgets[0].X == 117 && document.HybridWidgets[0].Y == 63 &&
            document.HybridWidgets[0].Name == "Clock" && document.HybridWidgets[0].ZIndex == 4,
            "Older Hybrid themes must undo the saved Duplicate offset, suffix and layer increment.");
        service.SaveToPath(document, ScreenMode.Hybrid, legacyPath);
        ModeThemeService.Apply(document, service.Load(legacyPath));
        Assert(document.HybridWidgets[0].X == 117 && document.HybridWidgets[0].Y == 63,
            "Resaving an older Hybrid theme must not apply its migration a second time.");
        var rejected = false;
        try { ModeThemeService.Apply(document, preset); } catch (InvalidDataException) { rejected = true; }
        Assert(rejected, "A theme from the wrong mode must be rejected.");
        var sessions = new ThemeSessionController();
        sessions.Loaded(ScreenMode.PhotoFrame, photoPath);
        sessions.MarkDirty(ScreenMode.InfoScreen);
        sessions.Loaded(ScreenMode.Hybrid, hybridPath);
        Assert(sessions.IsDirty(ScreenMode.InfoScreen) && sessions.HasUnsavedChanges, "Saving one mode must not discard another mode's unsaved state.");
        Assert(sessions.PathFor(ScreenMode.PhotoFrame) == photoPath && sessions.PathFor(ScreenMode.Hybrid) == hybridPath, "Save must keep separate active filenames for each mode.");
        var current = new PhotoFrameSettings { CaptionFontSize = 24, CaptionFontFamily = "Orbitron" };
        current.Photos.Add(photo);
        sessions.SwitchSettings(ScreenMode.PhotoFrame, ScreenMode.Hybrid, current);
        current.CaptionFontSize = 36;
        current.CaptionFontFamily = "Bungee";
        sessions.SwitchSettings(ScreenMode.Hybrid, ScreenMode.PhotoFrame, current);
        Assert(current.CaptionFontSize == 24 && current.Photos.Count == 1, "Returning to a mode must restore its settings while retaining the shared playlist.");
        Assert(current.CaptionFontFamily == "Orbitron", "Photo Frame must retain its independent caption font.");
        sessions.SwitchSettings(ScreenMode.PhotoFrame, ScreenMode.Hybrid, current);
        Assert(current.CaptionFontSize == 36, "Photo and Hybrid settings must remain independent in memory.");
        Assert(current.CaptionFontFamily == "Bungee", "Hybrid must retain its independent caption font.");
    }

    private static void CheckThemeFolderReload(string root)
    {
        var folder = Path.Combine(root, "album");
        var subfolder = Path.Combine(folder, "nested");
        Directory.CreateDirectory(subfolder);
        using var bitmap = new SKBitmap(1, 1);
        bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var bytes = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(folder, "one.png"), bytes.ToArray());
        File.WriteAllBytes(Path.Combine(subfolder, "two.png"), bytes.ToArray());
        File.WriteAllText(Path.Combine(folder, "ignore.txt"), "Not a photo");
        using var source = new ThemeWorkspace(new ThemeDocument(), Path.Combine(root, "folder-source"));
        using var photos = new PhotoPlaybackController(() => source, Settings(root, "folder-source"));
        photos.AddPhotoFolder(folder);
        Assert(source.Document.PhotoFrame.WatchedFolder == folder && !source.Document.PhotoFrame.WatchFolderEnabled,
            "Add folder must remember the folder without enabling automatic watching.");
        var service = new ModeThemeService(Path.Combine(root, "folder-themes"));
        foreach (var mode in new[] { ScreenMode.PhotoFrame, ScreenMode.Hybrid })
        {
            source.Document.Mode = mode;
            source.Document.HybridWidgets.Clear();
            if (mode == ScreenMode.Hybrid)
                source.Document.HybridWidgets.Add(new WidgetModel { Name = "Overlay", X = 89, Y = 132 });
            var path = service.Save(source.Document, mode, mode.ToString());
            using var vm = new MainViewModel(Settings(root, "folder-load-" + mode));
            vm.SetEditorActive(false);
            vm.Document.Mode = mode;
            var infoWidgets = vm.Document.Widgets.ToArray();
            vm.LoadModeThemeFile(path);
            Assert(vm.Document.PhotoFrame.WatchedFolder == folder && vm.Document.PhotoFrame.Photos.Count == 2 &&
                !vm.Document.PhotoFrame.WatchFolderEnabled && !vm.IsDirty,
                "Opening a mode theme in a fresh app must restore its folder and photos even with watching off.");
            Assert(vm.Document.Widgets.SequenceEqual(infoWidgets), "Loading photo settings must not alter Info Screen layers.");
            if (mode == ScreenMode.Hybrid)
                Assert(vm.Document.HybridWidgets[0].X == 89 && vm.Document.HybridWidgets[0].Y == 132,
                    "Opening a Hybrid theme through the app must preserve overlay placement.");
            vm.LoadModeThemeFile(path);
            Assert(vm.Document.PhotoFrame.Photos.Count == 2 && !vm.IsDirty, "Reloading a folder theme must not duplicate photos or mark it dirty.");
        }
        var otherFolder = Path.Combine(root, "hybrid-album");
        Directory.CreateDirectory(otherFolder);
        File.WriteAllBytes(Path.Combine(otherFolder, "three.png"), bytes.ToArray());
        source.Document.PhotoFrame.WatchedFolder = otherFolder;
        var otherTheme = service.Save(source.Document, ScreenMode.Hybrid, "Other album");
        using (var vm = new MainViewModel(Settings(root, "folder-switch")))
        {
            vm.SetEditorActive(false);
            vm.Document.Mode = ScreenMode.PhotoFrame;
            vm.LoadModeThemeFile(Path.Combine(service.DirectoryFor(ScreenMode.PhotoFrame), "PhotoFrame.pcphoto"));
            vm.Document.Mode = ScreenMode.Hybrid;
            vm.LoadModeThemeFile(otherTheme);
            vm.Document.Mode = ScreenMode.PhotoFrame;
            Assert(vm.Document.PhotoFrame.WatchedFolder == folder && vm.Document.PhotoFrame.Photos.Count == 2,
                "Returning to Photo Frame must restore its own saved folder's photos.");
            vm.Document.Mode = ScreenMode.Hybrid;
            Assert(vm.Document.PhotoFrame.WatchedFolder == otherFolder && vm.Document.PhotoFrame.Photos.Count == 1 &&
                vm.Document.HybridWidgets[0].X == 89 && vm.Document.HybridWidgets[0].Y == 132 && !vm.HasUnsavedChanges,
                "Mode switching must retain Hybrid's own folder and exact layout without creating unsaved edits.");
        }
        source.Document.PhotoFrame.WatchedFolder = Path.Combine(root, "missing-album");
        Assert(photos.RestoreThemeFolder() is not null && source.Document.PhotoFrame.Photos.Count == 0 && photos.SelectedPhoto is null,
            "Missing folders must be reported without displaying photos from an unrelated album.");
    }

    private static void CheckDashboard()
    {
        var dashboard = new HardwareDashboardViewModel();
        dashboard.SetInventory(new Dictionary<string, MetricValue> { ["Hardware.CPUName"] = new(Text: "Sample processor") });
        Assert(dashboard.Cards[0].HardwareName == "Sample processor" && dashboard.Cards[0].Value == "Unavailable", "Hardware names should appear without inventing sensor values.");
        dashboard.Update(new Dictionary<string, MetricValue> { ["CPU.Usage"] = new(0, Unit: "%"), ["GPU.Usage"] = new(double.NaN), ["RAM.TotalGB"] = new(32, Unit: "GB") });
        Assert(dashboard.Cards[0].Value == "0 %" && dashboard.Cards[1].Value == "Unavailable", "A real zero must differ from a missing or invalid sensor reading.");
        Assert(dashboard.Cards[2].HardwareName.Contains("32 GB"), "RAM capacity should appear on the dashboard.");
        var firstRow = dashboard.Cards[0].Rows[0];
        dashboard.Update(new Dictionary<string, MetricValue> { ["CPU.Usage"] = new(0, Unit: "%"), ["RAM.TotalGB"] = new(32, Unit: "GB") });
        Assert(ReferenceEquals(firstRow, dashboard.Cards[0].Rows[0]), "Unchanged readings should not rebuild dashboard rows.");
    }

    private static void CheckPageNavigation(string root)
    {
        using var vm = new MainViewModel(Settings(root, "navigation"));
        vm.SetEditorActive(false); // Avoid accessing physical sensors in this check.
        Assert(vm.UseLiveData, "Live sensor data must be enabled from startup.");
        vm.Document.Mode = ScreenMode.Hybrid;
        Assert(!vm.IsDirty, "Switching screen modes alone must not create unsaved theme edits.");
        vm.Document.HybridWidgets.Add(new WidgetModel { Type = WidgetType.Text, Label = "Unsaved overlay" });
        var document = vm.Document;
        var selected = vm.SelectedWidget;
        var dirty = vm.IsDirty;
        var liveData = vm.UseLiveData;
        var liveMode = vm.IsLiveMode;
        vm.ShowPage(true);
        Assert(vm.IsHardwarePage && !vm.IsCanvasActive && !vm.DeleteWidgetCommand.CanExecute(null), "Hardware must suspend editor work and editing shortcuts.");
        Assert(ReferenceEquals(document, vm.Document) && vm.Document.Mode == ScreenMode.Hybrid && vm.IsDirty == dirty && vm.UseLiveData == liveData && vm.IsLiveMode == liveMode, "Hardware navigation must preserve mode, theme, unsaved edits, and view preferences.");
        vm.ShowPage(false);
        Assert(vm.IsEditorPage && ReferenceEquals(selected, vm.SelectedWidget), "Returning to Editor must retain selection.");
    }

    private static SettingsController Settings(string root, string name)
        => new(new AppSettingsService(Path.Combine(root, name + ".json")));

    private static void CheckSettings(string root)
    {
        var settings = Settings(root, "preferences");
        var notifications = 0;
        settings.PropertyChanged += (_, _) => notifications++;
        settings.CloseToTray = false;
        settings.CloseToTray = false;
        Assert(notifications == 1, "Unchanged preferences must not emit another notification.");
        settings.CanvasZoom = 20;
        Assert(settings.CanvasZoom == 3, "Canvas zoom upper limit changed.");
        settings.CanvasZoom = 0;
        Assert(settings.CanvasZoom == .5, "Canvas zoom lower limit changed.");
        settings.LastPhotoIndex = 2;
        var reloaded = Settings(root, "preferences");
        Assert(!reloaded.CloseToTray && reloaded.CanvasZoom == .5 && reloaded.LastPhotoIndex == 2,
            "Preferences did not persist independently of the editor.");
    }

    private static void CheckEditor()
    {
        var document = new ThemeDocument { SnapToGrid = true, GridSize = 10 };
        var editor = new EditorController(() => document);
        var first = new WidgetModel { X = 20, Y = 20, Width = 30, Height = 30, FontSize = 24 };
        var second = new WidgetModel { X = 60, Y = 20, Width = 30, Height = 30 };
        document.Widgets.Add(first);
        document.Widgets.Add(second);
        editor.SelectWidgets([first, second], additive: false);
        editor.GroupSelected();
        Assert(first.GroupId is not null && first.GroupId == second.GroupId, "Grouping failed.");
        editor.SelectWidget(first);
        Assert(editor.SelectedWidgetCount == 2, "Selecting a grouped widget lost the group selection.");
        editor.NudgeSelected("Right:10");
        Assert(first.X == 30 && second.X == 70 && first.FontSize == 24, "Grid movement changed positions or font size.");
        second.IsLocked = true;
        editor.NudgeSelected("Down:10");
        Assert(first.Y == 30 && second.Y == 20, "Locked widgets moved.");
        editor.DuplicateSelected();
        Assert(document.Widgets.Count == 4 && editor.SelectedWidgetCount == 2, "Multi-selection duplication failed.");
        Assert(editor.SelectedWidgets[0].GroupId != first.GroupId, "Duplicated widgets retained the original group.");
        editor.DeleteSelected();
        Assert(document.Widgets.Count == 2 && editor.SelectedWidget is null, "Delete did not clear selection.");

        editor.ApplySmartAlignment(first, 2, 20, [first, second]);
        Assert(editor.AlignmentGuideX == 0, "Canvas alignment guide changed.");
        editor.ClearAlignmentGuides();
        Assert(editor.AlignmentGuideX is null && editor.AlignmentGuideY is null, "Guides did not clear.");

        document.Mode = ScreenMode.Hybrid;
        editor.AddWidgetToCanvas(WidgetType.Value, "GPU.Temperature");
        Assert(document.HybridWidgets.Count == 1 && document.Widgets.Count == 2,
            "Hybrid editing altered Info Screen widgets.");
        Assert(editor.SelectedWidget?.DataSource == "GPU.Temperature", "Search-add lost the requested source.");
        editor.AddWidgetToCanvas(WidgetType.Shape, null);
        Assert(document.HybridWidgets.Count == 2 && editor.SelectedWidget?.Type == WidgetType.Shape && document.Widgets.Count == 2,
            "Adding a Hybrid Shape must create an overlay without changing Info Screen layers.");
        document = new ThemeDocument();
        editor.AddWidgetToCanvas(WidgetType.Text, null);
        Assert(document.Widgets.Count == 1, "Editor kept a stale document after workspace replacement.");
    }

    private static void CheckWidgetBackground(string root)
    {
        foreach (var type in new[] { WidgetType.Text, WidgetType.Shape })
        {
            var document = new ThemeDocument { CanvasWidth = 100, CanvasHeight = 80, BackgroundColor = "#FF000000" };
            using var workspace = new ThemeWorkspace(document, Path.Combine(root, "background-" + type));
            var widget = new WidgetModel
            {
                Type = type, X = 0, Y = 0, Width = 90, Height = 70, CornerRadius = 0,
                BackgroundColor = "#FF0000FF", ForegroundColor = "#FF00FF00", AccentColor = "#FFFF0000", Label = "X"
            };
            document.Widgets.Add(widget);
            var renderer = new PCInfoScreenStudio.Rendering.ThemeRenderer();
            using var solid = renderer.Render(workspace);
            widget.BackgroundTransparency = 50;
            using var half = renderer.Render(workspace);
            widget.BackgroundTransparency = 100;
            using var clear = renderer.Render(workspace);
            Assert(solid.GetPixel(6, 6).Blue == 255 && Math.Abs(half.GetPixel(6, 6).Blue - 128) <= 1 && clear.GetPixel(6, 6) == SKColors.Black,
                "Background transparency must make text and shape fills solid, half-transparent or invisible.");
            var foreground = type == WidgetType.Text ? SKColors.Lime : SKColors.Red;
            var solidPixels = solid.Pixels.Count(pixel => pixel == foreground);
            Assert(solidPixels > 0 && half.Pixels.Count(pixel => pixel == foreground) == solidPixels &&
                clear.Pixels.Count(pixel => pixel == foreground) == solidPixels,
                "Changing background transparency must leave visible text or shape outlines unchanged.");
            widget.BackgroundTransparency = 50;
            var copy = widget.Clone();
            var reloaded = System.Text.Json.JsonSerializer.Deserialize<WidgetModel>(System.Text.Json.JsonSerializer.Serialize(widget))!;
            Assert(copy.BackgroundTransparency == 50 && reloaded.BackgroundTransparency == 50 && reloaded.ForegroundColor == "#FF00FF00",
                "Background transparency must survive duplication and serialization without fading the foreground.");
            widget.BackgroundTransparency = -1;
            Assert(widget.BackgroundTransparency == 0, "Background transparency must be bounded at zero.");
            widget.BackgroundTransparency = 101;
            Assert(widget.BackgroundTransparency == 100, "Background transparency must be bounded at 100.");
        }
    }

    private static void CheckPhotos(string root)
    {
        using var workspace = new ThemeWorkspace(new ThemeDocument(), Path.Combine(root, "photo-workspace"));
        var now = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);
        var preferences = Settings(root, "photos");
        using var photos = new PhotoPlaybackController(() => workspace, preferences, () => now);
        var settings = workspace.Document.PhotoFrame;
        settings.Photos.Add(new PhotoFrameItem { SourcePath = Path.Combine(root, "one.png") });
        settings.Photos.Add(new PhotoFrameItem { SourcePath = Path.Combine(root, "two.png") });
        settings.Photos.Add(new PhotoFrameItem { SourcePath = Path.Combine(root, "three.png") });
        settings.DefaultDurationSeconds = 5;
        settings.TransitionDurationSeconds = 1;
        photos.InitializePhotoFrameRuntime();
        photos.NextPhoto();
        Assert(settings.RuntimeCurrentIndex == 1 && preferences.LastPhotoIndex == 1, "Next photo did not update position.");
        var current = settings.Photos[1];
        photos.MovePhoto(current, settings.Photos[0]);
        Assert(ReferenceEquals(settings.Photos[settings.RuntimeCurrentIndex], current), "Reordering changed the displayed photo.");
        now = now.AddSeconds(6);
        Assert(photos.UpdatePhotoPlayback() && settings.RuntimeCurrentIndex == 1, "Playback timer did not advance.");
        photos.TogglePhotoPlayback();
        var pausedIndex = settings.RuntimeCurrentIndex;
        now = now.AddSeconds(20);
        photos.UpdatePhotoPlayback();
        Assert(settings.RuntimeCurrentIndex == pausedIndex && !preferences.PhotoFramePlaying, "Paused playback advanced.");
        photos.PreviousPhoto();
        Assert(settings.RuntimeCurrentIndex == 0, "Manual navigation stopped working while paused.");
        photos.SetCurrentPhoto(2, manual: true);
        settings.Loop = false;
        settings.Shuffle = false;
        photos.TogglePhotoPlayback();
        now = now.AddSeconds(6);
        photos.UpdatePhotoPlayback();
        Assert(!photos.IsPhotoPlaying && settings.RuntimeCurrentIndex == 2, "Non-looping playback did not stop at the final photo.");
        workspace.Document.Mode = ScreenMode.Hybrid;
        photos.UpdateEffectiveScreenMode();
        Assert(workspace.Document.RuntimeMode == RuntimeScreenMode.Hybrid, "Hybrid runtime mode was not applied.");
    }

    private static void CheckDevice(string root)
    {
        var hardware = new FakeDisplayDevice();
        var document = new ThemeDocument();
        var settings = Settings(root, "device");
        using var controller = new DeviceController(() => document, settings, hardware,
            () => [new SerialPortOption("COM6", "Test screen", "", "", 3)]);
        controller.DetectScreen();
        Assert(controller.SelectedPort == "COM6", "Device discovery did not choose the screen.");
        Await(controller.ConnectOrDisconnectAsync());
        Assert(controller.IsConnected && controller.LivePreview && controller.DisplayActionLabel == "Stop display",
            "Connection state was not forwarded.");
        controller.DisplayProtocol = DisplayProtocolProfile.RevANativePortrait;
        Assert(settings.DisplayProtocol == DisplayProtocolProfile.RevANativePortrait && hardware.CompatibilityCalls == 1,
            "Device compatibility did not persist or apply.");
        using var bitmap = new SKBitmap(2, 2);
        hardware.BlockFirstFrame = true;
        controller.SendLiveFrame(bitmap);
        document.DeviceRotation = DeviceRotation.Degrees90;
        controller.SendLiveFrame(bitmap);
        document.DeviceRotation = DeviceRotation.Degrees180;
        controller.SendLiveFrame(bitmap);
        Assert(hardware.Frames.Count == 1, "Frames were sent concurrently.");
        hardware.ReleaseFirstFrame();
        PumpUntil(() => hardware.Frames.Count == 2);
        Assert(hardware.Frames[1] == DeviceRotation.Degrees180, "The queue retained an outdated pending frame.");
        Await(controller.ConnectOrDisconnectAsync());
        Assert(!controller.IsConnected && !controller.LivePreview && controller.DisplayActionLabel == "Start display",
            "Disconnect state was not forwarded.");
        controller.Dispose();
        controller.SendLiveFrame(bitmap, force: true);
        Assert(hardware.Disposed && hardware.Frames.Count == 2, "Disposed device accepted a new frame.");
    }

    private static void Await(Task task)
    {
        PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    private static void PumpUntil(Func<bool> completed)
    {
        var deadline = Stopwatch.StartNew();
        while (!completed())
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Controller check timed out.");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }

    private static void ThemeRendererCleanup() => PCInfoScreenStudio.Rendering.ThemeRenderer.ClearCaches();

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FakeDisplayDevice : IDisplayDevice
    {
        private readonly TaskCompletionSource _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsConnected { get; private set; }
        public string? ConnectedPort => IsConnected ? "COM6" : null;
        public int? ConnectedBaudRate => 921600;
        public string? ConnectedModel => "Controller check";
        public DisplayProtocolProfile ConnectedProtocol => DisplayProtocolProfile.Auto;
        public DisplayColorMode ConnectedColorMode => DisplayColorMode.Auto;
        public bool BlockFirstFrame { get; set; }
        public bool Disposed { get; private set; }
        public int CompatibilityCalls { get; private set; }
        public List<DeviceRotation> Frames { get; } = [];
        public Task ConnectAsync(string portName, ThemeOrientation theme, DeviceRotation rotation,
            DisplayProtocolProfile requestedProtocol, DisplayColorMode requestedColorMode,
            SerialPortOption? deviceInfo = null, CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }
        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = false;
            return Task.CompletedTask;
        }
        public Task ApplyCompatibilityAsync(DisplayProtocolProfile requestedProtocol, DisplayColorMode requestedColorMode,
            SerialPortOption? deviceInfo, ThemeOrientation theme, DeviceRotation rotation,
            CancellationToken cancellationToken = default)
        {
            CompatibilityCalls++;
            return Task.CompletedTask;
        }
        public Task ApplyOrientationAsync(ThemeOrientation theme, DeviceRotation rotation, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task DisplayAsync(SKBitmap bitmap, DeviceRotation rotation, CancellationToken cancellationToken = default)
        {
            Frames.Add(rotation);
            return BlockFirstFrame && Frames.Count == 1 ? _firstFrame.Task : Task.CompletedTask;
        }
        public Task TestPatternAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RunBenchmarkAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void ReleaseFirstFrame() => _firstFrame.TrySetResult();
        public void Dispose() => Disposed = true;
    }
}
