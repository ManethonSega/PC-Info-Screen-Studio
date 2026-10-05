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
            CheckPhotos(root);
            CheckDevice(root);
            CheckThemeModes(root);
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
        var photoPath = service.Save(document, ScreenMode.PhotoFrame, "Evening");
        Assert(photoPath.EndsWith(".pcphoto") && Path.GetDirectoryName(photoPath)!.EndsWith("Photo Frame Themes"), "Photo themes must use their own folder and extension.");
        var preset = service.Load(photoPath);
        Assert(preset.PhotoFrame.Photos.Count == 0 && !File.ReadAllText(photoPath).Contains("linked-only"), "Settings themes must never contain photos or playlist entries.");
        document.PhotoFrame.CaptionFontSize = 12;
        ModeThemeService.Apply(document, preset);
        Assert(document.PhotoFrame.Photos.Count == 1 && ReferenceEquals(document.PhotoFrame.Photos[0], photo), "Applying a theme must retain the original linked playlist.");
        Assert(document.PhotoFrame.CaptionFontSize == 24, "Applying a theme must update global caption settings.");
        document.Mode = ScreenMode.Hybrid;
        document.HybridWidgets.Add(new WidgetModel { Type = WidgetType.Text, Label = "Clock overlay" });
        var hybridPath = service.Save(document, ScreenMode.Hybrid, "Overlay");
        Assert(hybridPath.EndsWith(".pchybrid") && Path.GetDirectoryName(hybridPath)!.EndsWith("Hybrid Themes"), "Hybrid themes must use their own folder and extension.");
        var hybrid = service.Load(hybridPath);
        document.HybridWidgets.Clear();
        ModeThemeService.Apply(document, hybrid);
        Assert(document.HybridWidgets.Count == 1 && document.PhotoFrame.Photos.Count == 1, "Hybrid loading must restore overlays and retain photos.");
        var rejected = false;
        try { ModeThemeService.Apply(document, preset); } catch (InvalidDataException) { rejected = true; }
        Assert(rejected, "A theme from the wrong mode must be rejected.");
        var sessions = new ThemeSessionController();
        sessions.Loaded(ScreenMode.PhotoFrame, photoPath);
        sessions.MarkDirty(ScreenMode.InfoScreen);
        sessions.Loaded(ScreenMode.Hybrid, hybridPath);
        Assert(sessions.IsDirty(ScreenMode.InfoScreen) && sessions.HasUnsavedChanges, "Saving one mode must not discard another mode's unsaved state.");
        Assert(sessions.PathFor(ScreenMode.PhotoFrame) == photoPath && sessions.PathFor(ScreenMode.Hybrid) == hybridPath, "Save must keep separate active filenames for each mode.");
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
        vm.Document.Mode = ScreenMode.Hybrid;
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
        document = new ThemeDocument();
        editor.AddWidgetToCanvas(WidgetType.Text, null);
        Assert(document.Widgets.Count == 1, "Editor kept a stale document after workspace replacement.");
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
