using PCInfoScreenStudio.Models;
using SkiaSharp;

namespace PCInfoScreenStudio.Services.TuringUsb;

/// <summary>Full-frame compressed image output for the 1CBE:0050 smart-screen family.</summary>
public sealed class TuringUsbDisplayDevice(Func<ITuringUsbTransport>? transportFactory = null) : IDisplayDevice
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<ITuringUsbTransport> _factory = transportFactory ?? (() => new WinUsbTransport());
    private ITuringUsbTransport? _transport;
    private ThemeOrientation _orientation;
    private DeviceRotation _rotation;
    private bool _disposed;
    public bool IsConnected => _transport is not null;
    public string? ConnectedPort { get; private set; }
    public int? ConnectedBaudRate => null;
    public string? ConnectedModel => IsConnected ? "TURZX 5.2-inch USB (experimental)" : null;
    public DisplayProtocolProfile ConnectedProtocol => IsConnected ? DisplayProtocolProfile.TuringUsb52 : DisplayProtocolProfile.Auto;
    public DisplayColorMode ConnectedColorMode => DisplayColorMode.Auto;

    public Task ConnectAsync(string portName, ThemeOrientation theme, DeviceRotation rotation,
        DisplayProtocolProfile requestedProtocol, DisplayColorMode requestedColorMode,
        SerialPortOption? deviceInfo = null, CancellationToken cancellationToken = default)
        => RunAsync(() =>
        {
            DisconnectCore();
            if (!TuringUsbProtocol.IsDevice(portName)) throw new ArgumentException("Select the TURZX 5.2-inch USB device, rather than a COM port.");
            var transport = _factory();
            try
            {
                transport.Open(portName);
                RequireResponse(transport.Exchange(TuringUsbProtocol.Packet(10), cancellationToken));
                // Match the reference's 50% brightness mapping (0..102).
                RequireResponse(transport.Exchange(TuringUsbProtocol.Packet(14, new byte[] { 51 }), cancellationToken));
                _orientation = theme;
                _rotation = rotation;
                ConnectedPort = portName;
                _transport = transport;
            }
            catch { transport.Dispose(); throw; }
        }, cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
        => RunAsync(DisconnectCore, cancellationToken);

    public Task ApplyCompatibilityAsync(DisplayProtocolProfile requestedProtocol, DisplayColorMode requestedColorMode,
        SerialPortOption? deviceInfo, ThemeOrientation theme, DeviceRotation rotation, CancellationToken cancellationToken = default)
        => ApplyOrientationAsync(theme, rotation, cancellationToken);

    public Task ApplyOrientationAsync(ThemeOrientation theme, DeviceRotation rotation, CancellationToken cancellationToken = default)
        => RunAsync(() => { _orientation = theme; _rotation = rotation; }, cancellationToken);

    public Task DisplayAsync(SKBitmap bitmap, DeviceRotation rotation, CancellationToken cancellationToken = default)
        => RunAsync(() => SendFrame(bitmap, _orientation, rotation, cancellationToken), cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken = default)
        => RunAsync(() =>
        {
            if (_transport is null) return;
            using var black = new SKBitmap(TuringUsbProtocol.NativeWidth, TuringUsbProtocol.NativeHeight);
            black.Erase(SKColors.Black);
            SendFrame(black, ThemeOrientation.Portrait, DeviceRotation.Degrees0, cancellationToken);
            RequireResponse(_transport.Exchange(TuringUsbProtocol.Packet(14, new byte[] { 0 }), cancellationToken));
        }, cancellationToken);

    public Task TestPatternAsync(CancellationToken cancellationToken = default)
        => RunAsync(() =>
        {
            if (_transport is null) throw new InvalidOperationException("Connect the display first.");
            var landscape = _orientation == ThemeOrientation.Landscape;
            using var bitmap = new SKBitmap(landscape ? 1280 : 720, landscape ? 720 : 1280);
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint();
            var colors = new[] { SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.Cyan, SKColors.Magenta, SKColors.Yellow };
            for (var i = 0; i < colors.Length; i++)
            {
                paint.Color = colors[i];
                canvas.DrawRect(i * bitmap.Width / 6f, 0, bitmap.Width / 6f + 1, bitmap.Height, paint);
            }
            paint.Color = SKColors.White;
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 8;
            canvas.DrawRect(4, 4, bitmap.Width - 8, bitmap.Height - 8, paint);
            SendFrame(bitmap, _orientation, _rotation, cancellationToken);
        }, cancellationToken);

    public Task RunBenchmarkAsync(CancellationToken cancellationToken = default)
        => TestPatternAsync(cancellationToken);

    private void SendFrame(SKBitmap bitmap, ThemeOrientation orientation, DeviceRotation rotation, CancellationToken cancellationToken)
    {
        if (_transport is null) return;
        using var native = PrepareNativeFrame(bitmap, orientation, rotation);
        var (command, image) = EncodeFrame(native);
        RequireResponse(_transport.Exchange(TuringUsbProtocol.ImagePacket(command, image), cancellationToken));
    }

    // The exact-model Python reference sends landscape clockwise 90 degrees and
    // portrait 180 degrees. Device rotation is applied without stretching the image.
    public static SKBitmap PrepareNativeFrame(SKBitmap source, ThemeOrientation orientation, DeviceRotation rotation)
    {
        var angle = ((orientation == ThemeOrientation.Landscape ? 90 : 180) + (int)rotation) % 360;
        var swap = angle is 90 or 270;
        var width = swap ? source.Height : source.Width;
        var height = swap ? source.Width : source.Height;
        var scale = Math.Min(720f / width, 1280f / height);
        var target = new SKBitmap(720, 1280, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(target);
        canvas.Clear(SKColors.Black);
        canvas.Translate(360, 640);
        canvas.RotateDegrees(angle);
        canvas.Scale(scale);
        canvas.DrawBitmap(source, -source.Width / 2f, -source.Height / 2f);
        return target;
    }

    public static (byte Command, byte[] Image) EncodeFrame(SKBitmap bitmap)
    {
        using var pixels = bitmap.PeekPixels();
        // Physical dashboard output failed with the PNG-first encoder in testing.
        // Use the JPEG command for every mode, including small/simple dashboards,
        // so changing mode cannot change the device's image decoder. Start at high
        // quality and retain full chroma resolution for coloured text and gauges.
        foreach (var quality in new[] { 95, 90, 80, 65, 50, 30, 10 })
        {
            using var jpeg = pixels.Encode(new SKJpegEncoderOptions(quality,
                SKJpegEncoderDownsample.Downsample444, SKJpegEncoderAlphaOption.BlendOnBlack))
                ?? throw new IOException("Could not encode the display image.");
            if (jpeg.Size <= TuringUsbProtocol.MaxImageBytes) return (101, jpeg.ToArray());
        }
        throw new IOException("The display image could not be compressed to the screen's transfer limit.");
    }

    private static void RequireResponse(byte[] response)
    {
        if (!TuringUsbProtocol.ResponseSucceeded(response)) throw new IOException("The TURZX display did not acknowledge the USB command.");
    }

    private async Task RunAsync(Action operation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await Task.Run(operation, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private void DisconnectCore()
    {
        var transport = _transport;
        _transport = null;
        ConnectedPort = null;
        transport?.Dispose();
    }

    public void Dispose()
    {
        _gate.Wait();
        try { if (_disposed) return; _disposed = true; DisconnectCore(); }
        finally { _gate.Release(); }
    }
}
