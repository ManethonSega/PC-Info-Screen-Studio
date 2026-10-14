using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Reflection;
using PCInfoScreenStudio.Controllers;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Rendering;
using PCInfoScreenStudio.Services;
using PCInfoScreenStudio.Services.TuringUsb;
using SkiaSharp;

internal static class RegressionFixChecks
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcinfo-regressions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            CheckPlaylists();
            CheckLargeText(root);
            CheckGifMemory();
            await CheckModeQueue(root);
            await CheckUsbModes();
            await CheckModeDoesNotReinitializeUsb(root);
        }
        finally { ThemeRenderer.ClearCaches(); Directory.Delete(root, true); }
    }

    private static void CheckPlaylists()
    {
        var sessions = new ThemeSessionController();
        var current = new PhotoFrameSettings { CaptionFontSize = 24, WatchedFolder = "photo-folder" };
        current.Photos.Add(new PhotoFrameItem { SourcePath = "photo-a.jpg", DisplayName = "Photo" });
        sessions.SwitchSettings(ScreenMode.PhotoFrame, ScreenMode.Hybrid, current);
        Assert(current.Photos.Count == 0, "A new Hybrid playlist must not inherit Photo Frame photos.");
        current.Photos.Add(new PhotoFrameItem { SourcePath = "hybrid-a.jpg", DisplayName = "Hybrid" });
        current.Photos.Add(new PhotoFrameItem { SourcePath = "hybrid-b.jpg" });
        current.RuntimeCurrentIndex = 1;
        current.CaptionFontSize = 80;
        current.WatchedFolder = "hybrid-folder";
        sessions.SwitchSettings(ScreenMode.Hybrid, ScreenMode.InfoScreen, current);
        sessions.SwitchSettings(ScreenMode.InfoScreen, ScreenMode.PhotoFrame, current);
        Assert(current.Photos.Single().SourcePath == "photo-a.jpg" && current.CaptionFontSize == 24 && current.WatchedFolder == "photo-folder", "Info Screen detour must restore Photo Frame's own photos and settings.");
        current.Photos[0].DisplayName = "Edited Photo";
        var state = JsonSerializer.Deserialize<ThemeSessionState>(JsonSerializer.Serialize(sessions.Capture(ScreenMode.PhotoFrame, current)))!;
        var restored = new ThemeSessionController(); restored.Restore(state);
        restored.SwitchSettings(ScreenMode.PhotoFrame, ScreenMode.Hybrid, current);
        Assert(current.Photos.Count == 2 && current.Photos[0].DisplayName == "Hybrid" && current.RuntimeCurrentIndex == 1 && current.CaptionFontSize == 80, "Resume must retain independent Hybrid items and playback position.");
        current.Photos.Clear();
        restored.SwitchSettings(ScreenMode.Hybrid, ScreenMode.PhotoFrame, current);
        Assert(current.Photos.Count == 1 && current.Photos[0].DisplayName == "Edited Photo", "Clearing one playlist must not alter the other.");
        Assert(ModeThemeService.ExtensionFor(ScreenMode.PhotoFrame) == ".pcphoto" && ModeThemeService.ExtensionFor(ScreenMode.Hybrid) == ".pchybrid", "Mode theme extensions remain distinct.");
        Console.WriteLine("PASS: independent photos, settings, item edits, Info detours, persisted positions and clearing.");
    }

    private static void CheckLargeText(string root)
    {
        using var workspace = new ThemeWorkspace(new ThemeDocument { CanvasWidth = 1280, CanvasHeight = 720, BackgroundColor = "#FF000000" }, Path.Combine(root, "render"));
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "Orbitron-Variable.ttf");
        Assert(File.Exists(fontPath), "The actual app font must be available for render checks.");
        // Both the portable check and Windows output copy the app's actual font.
        var asset = new ThemeAsset { Kind = ThemeAssetKind.Font, LocalPath = fontPath };
        workspace.Document.Assets.Add(asset);
        var renderer = new ThemeRenderer();
        foreach (var type in new[] { WidgetType.BarGauge, WidgetType.Graph })
        foreach (var size in new[] { 22d, 70d, 80d })
        {
            workspace.Document.Widgets.Clear();
            var widget = new WidgetModel { Type = type, X = 50, Y = 40, Width = 600, Height = 200, FontSize = size, FontAssetId = asset.Id, ShowValue = false, Label = "CPU Usage", ForegroundColor = "#FFFFFFFF", AccentColor = "#FFFFB43A", BackgroundColor = "#FF000000" };
            workspace.Document.Widgets.Add(widget);
            using var bitmap = renderer.Render(workspace);
            using var expectedTypeface = SKTypeface.FromFile(fontPath)!;
            using var font = new SKFont(expectedTypeface, Math.Max(8, (float)size * .45f));
            var expectedBaseline = 4 - Math.Min(0, font.Metrics.Top);
            using var expected = new SKBitmap(600, 200); expected.Erase(SKColors.Black);
            using (var canvas = new SKCanvas(expected))
            using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true })
                canvas.DrawText("CPU Usage", 4, expectedBaseline, SKTextAlign.Left, font, paint);
            var expectedWhite = 0; var actualWhite = 0;
            for (var y = 0; y < 150; y++) for (var x = 0; x < 600; x++)
            {
                if (expected.GetPixel(x, y).Red > 200) expectedWhite++;
                var pixel = bitmap.GetPixel(50 + x, 40 + y);
                if (pixel.Red > 200 && pixel.Green > 200 && pixel.Blue > 200) actualWhite++;
            }
            Assert(expectedWhite > 0 && actualWhite == expectedWhite, $"{type} font {size}: header glyphs must be complete inside the widget.");
            widget.Height = 250;
            using var resized = renderer.Render(workspace);
            Assert(widget.FontSize == size && widget.Height == 250, "Resizing must remain independent of font size.");
        }
        Console.WriteLine("PASS: real graph/bar rendering at 22, 70 and 80, complete glyphs, independent geometry.");
    }

    private static void CheckGifMemory()
    {
        var type = typeof(AnimatedGifFrameProvider).GetNestedType("GifState", BindingFlags.NonPublic)!;
        foreach (var disposal in new[] { 1, 2, 3 })
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", $"disposal-{disposal}.gif");
            var state = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { path }, null)!;
            try
            {
                Assert(type.GetField("_encodedData", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state) is SKData, "Compressed GIF data must reside in RAM.");
                using var reference = SKCodec.Create(path)!;
                var info = new SKImageInfo(160, 100, SKColorType.Bgra8888, SKAlphaType.Premul);
                foreach (var speed in new[] { 1d, 2d, 4d })
                foreach (var elapsed in new[] { 35, 535, 1835, 1235, 2335, 2435, 5035, 835 })
                {
                    type.GetField("_startedAtMs", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(state, Environment.TickCount64 - (long)(elapsed / speed));
                    var actual = (SKBitmap)type.GetMethod("GetFrame")!.Invoke(state, new object[] { speed, 60, true })!;
                    using var expected = new SKBitmap(info); expected.Erase(SKColors.Transparent);
                    var result = reference.GetPixels(info, expected.GetPixels(), new SKCodecOptions((elapsed % 2400) / 100));
                    Assert(result == SKCodecResult.Success && actual.Pixels.SequenceEqual(expected.Pixels), "RAM decoding must preserve exact GIF skip/loop/disposal composition.");
                }
                if (OperatingSystem.IsLinux())
                {
                    static long ReadCharacters() => long.Parse(File.ReadAllLines("/proc/self/io").Single(l => l.StartsWith("rchar:")).Split(':')[1].Trim());
                    var before = ReadCharacters();
                    for (var i = 0; i < 300; i++)
                    {
                        type.GetField("_startedAtMs", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(state, Environment.TickCount64 - (i % 24 * 100 + 35));
                        type.GetMethod("GetFrame")!.Invoke(state, new object[] { 1d, 60, true });
                    }
                    var read = ReadCharacters() - before;
                    Assert(read < 2048, "Warm GIF decoding must not repeatedly read the source file.");
                    Console.WriteLine($"PASS: GIF disposal {disposal}, 300 RAM decodes, {read} process read bytes (includes reading the counter itself).");
                }
            }
            finally { ((IDisposable)state).Dispose(); }
        }
        Console.WriteLine("PASS: RAM GIF playback matches reference composition at speeds 1, 2 and 4.");
    }

    private static async Task CheckModeQueue(string root)
    {
        var document = new ThemeDocument { Mode = ScreenMode.PhotoFrame };
        var settings = new SettingsController(new AppSettingsService(Path.Combine(root, "settings.json"))) { AutoStartDisplay = false, PreferredCanvasSize = DisplayCanvasSize.Turing52 };
        var transport = new BlockingDisplay();
        var port = new SerialPortOption("usb#vid_1cbe&pid_0050#one", "TURZX", "usb-one", "Test", 100);
        using var controller = new DeviceController(() => document, settings, transport, () => new[] { port });
        controller.RefreshPorts(); await controller.ConnectOrDisconnectAsync();
        using var frame = new SKBitmap(1280, 720); frame.Erase(SKColors.Red);
        controller.SendLiveFrame(frame);
        await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // A held transmission represents a slow/long-running old playback
        // session. All waiting requests must collapse to the latest new mode.
        for (var i = 0; i < 30; i++)
        {
            document.Mode = i % 2 == 0 ? ScreenMode.Hybrid : ScreenMode.PhotoFrame;
            controller.NotifyModeChanged(); controller.SendLiveFrame(frame);
        }
        document.Mode = ScreenMode.InfoScreen;
        frame.Erase(SKColors.Blue);
        controller.NotifyModeChanged(); controller.SendLiveFrame(frame);
        transport.Release.TrySetResult();
        await transport.NewModeShown.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert(transport.Frames.Last() == ScreenMode.InfoScreen && controller.IsConnected,
            "A delayed old frame must not strand the sender or overwrite the latest mode.");
        Console.WriteLine("PASS: delayed transmission, 30 queued mode changes and newest-mode output without reinitializing USB.");
    }

    private static async Task CheckUsbModes()
    {
        var transport = new ImageTransport { RejectHandshakeDuringPlayback = true };
        using var device = new TuringUsbDisplayDevice(() => transport);
        await device.ConnectAsync("usb#vid_1cbe&pid_0050#one", ThemeOrientation.Landscape, DeviceRotation.Degrees0, DisplayProtocolProfile.Auto, DisplayColorMode.Auto);
        using var bitmap = new SKBitmap(1280, 720); bitmap.Erase(SKColors.Cyan);
        foreach (var mode in new[] { ScreenMode.PhotoFrame, ScreenMode.InfoScreen, ScreenMode.Hybrid, ScreenMode.InfoScreen })
        {
            var start = transport.Commands.Count;
            for (var i = 0; i < 25; i++) await device.DisplayAsync(bitmap, DeviceRotation.Degrees0);
            Assert(transport.Commands.Skip(start).All(c => c == 101), "Mode content must use only bounded JPEG frames on the existing USB connection.");
        }
        Assert(transport.Frames == 100, "All repeated frame payloads must decode at native size.");
        Assert(transport.Commands.Take(2).SequenceEqual(new byte[] { 10, 14 }), "The handshake remains limited to connection initialization.");
        Console.WriteLine("PASS: one connection handshake and 100 decoded JPEG transfers across all modes (simulated transport).");
    }

    private static void Assert(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

    private static async Task CheckModeDoesNotReinitializeUsb(string root)
    {
        var document = new ThemeDocument { Mode = ScreenMode.PhotoFrame };
        var settings = new SettingsController(new AppSettingsService(Path.Combine(root, "mode-switch-settings.json")))
            { AutoStartDisplay = false, PreferredCanvasSize = DisplayCanvasSize.Turing52 };
        var transport = new ImageTransport { RejectHandshakeDuringPlayback = true };
        using var driver = new TuringUsbDisplayDevice(() => transport);
        var port = new SerialPortOption("usb#vid_1cbe&pid_0050#one", "TURZX", "usb-one", "Test", 100);
        using var controller = new DeviceController(() => document, settings, driver, () => new[] { port });
        controller.RefreshPorts(); await controller.ConnectOrDisconnectAsync();
        using var frame = new SKBitmap(1280, 720);
        frame.Erase(SKColors.Red);
        controller.SendLiveFrame(frame);
        await WaitForSender(controller);
        Assert(controller.IsConnected && transport.Frames == 1, "Initial playback must be connected.");
        document.Mode = ScreenMode.InfoScreen;
        controller.NotifyModeChanged();
        frame.Erase(SKColors.Blue);
        controller.SendLiveFrame(frame);
        await WaitForSender(controller);
        Assert(controller.IsConnected && controller.LivePreview && controller.DisplayActionLabel == "Stop display" && transport.Frames == 2,
            "Changing mode must not reinitialize playback, disconnect, or turn Stop display into Start display. " + controller.DeviceStatus);
        Assert(transport.Colors.Last().Blue >= 250 && transport.Colors.Last().Red < 5, "The new mode's image, rather than the previous frame, must reach the transport.");
        for (var i = 0; i < 48; i++)
        {
            document.Mode = (ScreenMode)(i % 3);
            controller.NotifyModeChanged();
            var color = i % 3 == 0 ? SKColors.Red : i % 3 == 1 ? SKColors.Lime : SKColors.Blue;
            frame.Erase(color);
            controller.SendLiveFrame(frame);
            await WaitForSender(controller);
            Assert(controller.IsConnected && controller.LivePreview && controller.DisplayActionLabel == "Stop display", "Repeated mode switches must leave the existing display session running.");
        }
        Assert(transport.Frames == 50 && transport.Commands.Take(2).SequenceEqual(new byte[] { 10, 14 }) && transport.Commands.Skip(2).All(c => c == 101),
            "The entire mode-switch run must contain one initial handshake followed only by image transfers.");
        Console.WriteLine("PASS: 50 mode frames keep Stop display and deliver new content with firmware that rejects reinitialization during playback.");
    }

    private static async Task WaitForSender(DeviceController controller)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var sync = typeof(DeviceController).GetField("_frameQueueSync", flags)!.GetValue(controller)!;
        var running = typeof(DeviceController).GetField("_frameSenderRunning", flags)!;
        var limit = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            lock (sync) { if (!(bool)running.GetValue(controller)!) return; }
            if (DateTime.UtcNow >= limit) throw new TimeoutException("The display sender did not finish.");
            await Task.Delay(1);
        }
    }

    private sealed class ImageTransport : ITuringUsbTransport
    {
        public List<byte> Commands { get; } = [];
        public List<SKColor> Colors { get; } = [];
        public int Frames { get; private set; }
        public bool RejectHandshakeDuringPlayback { get; init; }
        public void Open(string path) { }
        public byte[] Exchange(byte[] payload, CancellationToken cancellationToken)
        {
            using var des = DES.Create(); des.Key = "slv3tuzx"u8.ToArray();
            var command = des.DecryptCbc(payload.AsSpan(0, 504), "slv3tuzx"u8, PaddingMode.None)[0]; Commands.Add(command);
            if (RejectHandshakeDuringPlayback && Frames > 0 && command is 10 or 14)
                return new byte[] { command, 0 };
            if (command == 101)
            {
                using var image = SKBitmap.Decode(payload.AsSpan(512).ToArray());
                Assert(image.Width == 720 && image.Height == 1280 && payload.Length <= 512 + TuringUsbProtocol.MaxImageBytes, "JPEG size and payload budget."); Frames++;
                Colors.Add(image.GetPixel(360, 640));
            }
            return new byte[] { command, 0xc8 };
        }
        public void Dispose() { }
    }

    private sealed class BlockingDisplay : IDisplayDevice
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource NewModeShown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<ScreenMode> Frames { get; } = [];
        public bool IsConnected { get; private set; }
        public string? ConnectedPort { get; private set; }
        public int? ConnectedBaudRate => null; public string? ConnectedModel => "TURZX test";
        public DisplayProtocolProfile ConnectedProtocol => DisplayProtocolProfile.TuringUsb52;
        public DisplayColorMode ConnectedColorMode => DisplayColorMode.Auto;
        public Task ConnectAsync(string port, ThemeOrientation orientation, DeviceRotation rotation, DisplayProtocolProfile protocol, DisplayColorMode color, SerialPortOption? info = null, CancellationToken ct = default) { IsConnected = true; ConnectedPort = port; return Task.CompletedTask; }
        public Task DisconnectAsync(CancellationToken ct = default) { IsConnected = false; return Task.CompletedTask; }
        public Task ApplyCompatibilityAsync(DisplayProtocolProfile p, DisplayColorMode c, SerialPortOption? i, ThemeOrientation o, DeviceRotation r, CancellationToken ct = default) => Task.CompletedTask;
        public Task ApplyOrientationAsync(ThemeOrientation o, DeviceRotation r, CancellationToken ct = default) => Task.CompletedTask;
        public async Task DisplayAsync(SKBitmap bitmap, DeviceRotation rotation, CancellationToken ct = default)
        {
            var mode = bitmap.GetPixel(0, 0) == SKColors.Blue ? ScreenMode.InfoScreen : ScreenMode.PhotoFrame;
            if (!Started.Task.IsCompleted) { Started.TrySetResult(); await Release.Task; }
            Frames.Add(mode);
            if (mode == ScreenMode.InfoScreen) NewModeShown.TrySetResult();
        }
        public Task ClearAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task TestPatternAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RunBenchmarkAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { Release.TrySetResult(); }
    }
}
