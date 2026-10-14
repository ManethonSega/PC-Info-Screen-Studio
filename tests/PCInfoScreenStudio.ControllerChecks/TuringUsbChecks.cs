using System.IO;
using System.Buffers.Binary;
using System.Security.Cryptography;
using PCInfoScreenStudio.Controllers;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;
using PCInfoScreenStudio.Services.TuringUsb;
using SkiaSharp;

internal static class TuringUsbChecks
{
    public static void Check(string root, Action<Task> awaitTask)
    {
        var packet = TuringUsbProtocol.Packet(10, milliseconds: 123456);
        Assert(packet.Length == 512 && packet[510] == 0xA1 && packet[511] == 0x1A, "USB command must have its 512-byte envelope and trailer.");
        Assert(Convert.ToHexString(packet.AsSpan(0, 32)).ToLowerInvariant() == "9868916f8a3d5bf31021ddaf723f2d3e0a6569ea4dd6228b5849e463a370c8c4", "DES bytes must match the independently calculated OpenSSL reference.");
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        var image = TuringUsbProtocol.ImagePacket(102, payload);
        var header = Decrypt(image);
        Assert(header[0] == 102 && BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(8, 4)) == 5 && image.AsSpan(512).SequenceEqual(payload), "Image header length must be big-endian and followed by the exact encoded image.");
        Assert(!TuringUsbProtocol.ResponseSucceeded([]) && !TuringUsbProtocol.ResponseSucceeded(new byte[512]), "Missing and failed USB replies must not be accepted.");
        Assert(TuringUsbProtocol.ResponseSucceeded(new byte[] { 10, 0xC8 }), "Success acknowledgement must be recognized.");

        using var source = new SKBitmap(1280, 720);
        source.Erase(SKColors.Red);
        using (var native = TuringUsbDisplayDevice.PrepareNativeFrame(source, ThemeOrientation.Landscape, DeviceRotation.Degrees0))
        {
            Assert(native.Width == 720 && native.Height == 1280 && native.GetPixel(50, 50).Red == 255, "Landscape frames must fill the native portrait frame.");
            var encoded = TuringUsbDisplayDevice.EncodeFrame(native);
            using var decoded = SKBitmap.Decode(encoded.Image);
            Assert(encoded.Command == 101 && encoded.Image.Length <= TuringUsbProtocol.MaxImageBytes && decoded.Width == 720 && decoded.Height == 1280 && decoded.GetPixel(50, 50).Red >= 250,
                "Simple dashboards must use the same bounded, visible JPEG path as photos.");
        }
        using (var rotated = TuringUsbDisplayDevice.PrepareNativeFrame(source, ThemeOrientation.Landscape, DeviceRotation.Degrees90))
            Assert(rotated.GetPixel(360, 640).Red == 255 && rotated.GetPixel(0, 0) == SKColors.Black, "Quarter turns must preserve aspect ratio and letterbox rather than distort.");
        using (var dashboard = new SKBitmap(720, 1280))
        {
            dashboard.Erase(SKColors.Black);
            using var canvas = new SKCanvas(dashboard);
            using var paint = new SKPaint { Color = SKColors.Cyan };
            for (var y = 20; y < 1280; y += 30) canvas.DrawRect(35, y, 650, 4, paint);
            var encoded = TuringUsbDisplayDevice.EncodeFrame(dashboard);
            using var decoded = SKBitmap.Decode(encoded.Image);
            long error = 0;
            var before = dashboard.Pixels;
            var after = decoded.Pixels;
            for (var i = 0; i < before.Length; i++)
                error += Math.Abs(before[i].Red - after[i].Red) + Math.Abs(before[i].Green - after[i].Green) + Math.Abs(before[i].Blue - after[i].Blue);
            Assert(encoded.Command == 101 && error / (before.Length * 3d) < 3 && JpegUses444(encoded.Image),
                "Fine coloured dashboard lines must retain full chroma resolution and low average encoding error.");
        }
        using (var noise = new SKBitmap(720, 1280))
        {
            var random = new Random(42);
            var pixels = new SKColor[720 * 1280];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
            noise.Pixels = pixels;
            var encoded = TuringUsbDisplayDevice.EncodeFrame(noise);
            Assert(encoded.Command == 101 && encoded.Image.Length <= TuringUsbProtocol.MaxImageBytes, "Incompressible photos must reduce JPEG quality to stay within the payload budget.");
        }

        var transport = new FakeTransport();
        using var device = new TuringUsbDisplayDevice(() => transport);
        awaitTask(device.ConnectAsync("usb#vid_1cbe&pid_0050#one", ThemeOrientation.Landscape, DeviceRotation.Degrees0, DisplayProtocolProfile.Auto, DisplayColorMode.Auto));
        Assert(device.IsConnected && device.ConnectedBaudRate is null && transport.Commands.SequenceEqual(new byte[] { 10, 14 }), "WinUSB initialization must synchronize and set brightness, without serial reset or baud rate.");
        awaitTask(device.DisplayAsync(source, DeviceRotation.Degrees0));
        Assert(transport.Commands.Last() == 101, "The selected display must receive a JPEG image command for dashboard output.");
        for (var i = 0; i < 20; i++) awaitTask(device.DisplayAsync(source, DeviceRotation.Degrees0));
        Assert(transport.Commands.Skip(2).All(command => command == 101), "Repeated Info Screen updates must never switch to the PNG decoder.");
        foreach (var level in new[] { -10, 0, 1, 25, 50, 99, 100, 120 })
        {
            awaitTask(device.SetBrightnessAsync(level));
            Assert(transport.Commands.Last() == 14 && transport.Brightness.Last() == Math.Clamp(level, 0, 100) * 102 / 100,
                "Live USB brightness must use command 14 and the exact-family 0..102 scale.");
            awaitTask(device.DisplayAsync(source, DeviceRotation.Degrees0));
        }
        Assert(device.IsConnected && transport.Commands.Count(c => c == 10) == 1 && transport.Commands.Last() == 101,
            "Changing brightness must not send sync, reset the session or stop following JPEG output.");
        awaitTask(device.DisconnectAsync());
        Assert(!device.IsConnected && transport.Closed, "Disconnect must release USB access.");
        var savedTransport = new FakeTransport();
        using (var savedDevice = new TuringUsbDisplayDevice(() => savedTransport))
        {
            awaitTask(savedDevice.ConnectAsync("usb#vid_1cbe&pid_0050#two", ThemeOrientation.Landscape, DeviceRotation.Degrees0, DisplayProtocolProfile.Auto, DisplayColorMode.Auto, brightness: 75));
            Assert(savedTransport.Brightness.SequenceEqual(new byte[] { 76 }), "USB connection setup must apply the saved level directly, without flashing the 50% default first.");
        }
        transport.Fail = true;
        var failure = device.ConnectAsync("usb#vid_1cbe&pid_0050#one", ThemeOrientation.Landscape, DeviceRotation.Degrees0, DisplayProtocolProfile.Auto, DisplayColorMode.Auto);
        try { awaitTask(failure); throw new InvalidOperationException("Failed handshake was accepted."); }
        catch (Exception) when (failure.IsFaulted) { }
        Assert(!device.IsConnected && transport.Closed, "Failed handshakes must release the device and remain disconnected.");

        var document = new ThemeDocument();
        document.Widgets.Add(new WidgetModel { X = 48, Y = 32, Width = 96, Height = 64, FontSize = 20 });
        document.HybridWidgets.Add(new WidgetModel { X = 48, Y = 32, Width = 96, Height = 64 });
        var editor = new EditorController(() => document);
        editor.ResizeCanvas(DisplayCanvasSize.Turing52);
        Assert(document.CanvasWidth == 1280 && document.CanvasHeight == 720 && document.Widgets[0].X == 128 && document.HybridWidgets[0].Y == 72, "Canvas presets must scale both Info and Hybrid layers.");
        editor.ToggleOrientation();
        Assert(document.CanvasWidth == 720 && document.CanvasHeight == 1280, "Changing orientation must retain the new screen's resolution.");
        var json = System.Text.Json.JsonSerializer.Serialize(document);
        var reopened = System.Text.Json.JsonSerializer.Deserialize<ThemeDocument>(json)!;
        reopened.Orientation = ThemeOrientation.Landscape;
        Assert(reopened.CanvasWidth == 1280 && reopened.CanvasHeight == 720, "Resolution must survive serialization and a later orientation change.");
        var modes = new ModeThemeService(root);
        document.Mode = ScreenMode.Hybrid;
        var path = modes.Save(document, ScreenMode.Hybrid, "TURZX high resolution");
        var oldDocument = new ThemeDocument { Mode = ScreenMode.Hybrid };
        ModeThemeService.Apply(oldDocument, modes.Load(path));
        Assert(oldDocument.CanvasWidth == 720 && oldDocument.CanvasHeight == 1280 && oldDocument.HybridWidgets[0].Y == document.HybridWidgets[0].Y, "Hybrid themes must reopen at their saved resolution and exact overlay position.");
        var settings = new SettingsController(new AppSettingsService(Path.Combine(root, "usb-settings.json")))
        { AutoStartDisplay = false, PreferredCanvasSize = DisplayCanvasSize.Turing52 };
        var first = new SerialPortOption("usb#vid_1cbe&pid_0050#one#guid-a", "TURZX", "vid_1cbe&pid_0050\\one", "TURZX", 100);
        IReadOnlyList<SerialPortOption> discovered = new[] { first };
        using var controllerDevice = new TuringUsbDisplayDevice(() => new FakeTransport());
        using var controller = new DeviceController(() => oldDocument, settings, controllerDevice, () => discovered);
        controller.RefreshPorts();
        awaitTask(controller.ConnectOrDisconnectAsync());
        Assert(controller.DeviceStatus.Contains("compressed images") && !controller.DeviceStatus.Contains("baud"), "USB connection status must not show COM-specific details.");
        awaitTask(controller.ConnectOrDisconnectAsync());
        discovered = new[] { first with { PortName = "usb#vid_1cbe&pid_0050#one#guid-b" } };
        controller.RefreshPorts();
        Assert(controller.SelectedPort == discovered[0].PortName, "USB identity must survive an interface path change.");
        var smallHybrid = new ThemeDocument { Mode = ScreenMode.Hybrid };
        smallHybrid.HybridWidgets.Add(new WidgetModel { X = 48, Y = 32, FontSize = 20 });
        var smallPreset = modes.Load(modes.Save(smallHybrid, ScreenMode.Hybrid, "small authored"));
        var largeHybrid = new ThemeDocument { Mode = ScreenMode.Hybrid, CanvasWidth = 1280, CanvasHeight = 720 };
        largeHybrid.Widgets.Add(new WidgetModel { FontSize = 45 });
        ModeThemeService.ApplyForDisplay(largeHybrid, smallPreset, DisplayCanvasSize.Turing52);
        Assert(largeHybrid.HybridWidgets[0].X == 128 && largeHybrid.HybridWidgets[0].FontSize == 45 && largeHybrid.Widgets[0].FontSize == 45,
            "Adapting a preset to the active screen must scale imported layers once and preserve unrelated text size.");
        Console.WriteLine("PASS: TURZX packet encryption, dashboard/photo JPEG budgets, repeated dashboard updates, native frame orientation, failed-handshake cleanup, USB discovery identity and high-resolution theme persistence.");
    }
    private static byte[] Decrypt(byte[] packet)
    {
        using var des = DES.Create();
        des.Key = "slv3tuzx"u8.ToArray();
        return des.DecryptCbc(packet.AsSpan(0, 504), "slv3tuzx"u8, PaddingMode.None);
    }
    private static bool JpegUses444(byte[] bytes)
    {
        for (var i = 2; i < bytes.Length;)
        {
            if (bytes[i++] != 0xff) return false;
            var marker = bytes[i++];
            if (marker is 0xda or 0xd9) return false;
            var length = (bytes[i] << 8) | bytes[i + 1];
            if (marker == 0xc0)
                return bytes[i + 7] == 3 && bytes[i + 9] == 0x11 && bytes[i + 12] == 0x11 && bytes[i + 15] == 0x11;
            i += length;
        }
        return false;
    }
    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private sealed class FakeTransport : ITuringUsbTransport
    {
        public List<byte> Commands { get; } = [];
        public List<byte> Brightness { get; } = [];
        public bool Closed { get; private set; }
        public bool Fail { get; set; }
        public void Open(string path) { Closed = false; }
        public byte[] Exchange(byte[] payload, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var header = Decrypt(payload);
            Commands.Add(header[0]);
            if (header[0] == 14) Brightness.Add(header[8]);
            // Model a device whose working image command is JPEG, not PNG.
            return Fail || Commands.Last() == 102 ? new byte[512] : new byte[] { 10, 0xC8 };
        }
        public void Dispose() { Closed = true; }
    }
}
