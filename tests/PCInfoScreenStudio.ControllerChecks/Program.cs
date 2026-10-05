using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using PCInfoScreenStudio.Controllers;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;
using SkiaSharp;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        _ = new Application();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var root = Path.Combine(Path.GetTempPath(), "PCInfoScreenStudio-controller-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            CheckSettings(root);
            CheckEditor();
            CheckPhotos(root);
            CheckDevice(root);
            Console.WriteLine("PASS: preferences, editor operations, photo playback, and device queue checks.");
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
