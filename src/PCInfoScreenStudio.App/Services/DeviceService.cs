using System.IO.Ports;
using SkiaSharp;
using Tedd.TuringScreen;
using PCInfoScreenStudio.Models;
using TuringScreenOrientation = Tedd.TuringScreen.ScreenOrientation;

namespace PCInfoScreenStudio.Services;

public sealed class DeviceService : IDisposable
{
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private TuringScreen? _screen;
    private bool _disposed;

    public bool IsConnected => _screen is not null;
    public string? ConnectedPort { get; private set; }
    public int? ConnectedBaudRate { get; private set; }
    public string? ConnectedModel { get; private set; }
    public DisplayProtocolProfile ConnectedProtocol { get; private set; } = DisplayProtocolProfile.Auto;
    public DisplayColorMode ConnectedColorMode { get; private set; } = DisplayColorMode.Auto;

    public IReadOnlyList<string> GetPorts()
        => SerialPort.GetPortNames().OrderBy(ParsePortNumber).ToArray();

    public async Task ConnectAsync(
        string portName,
        ThemeOrientation theme,
        DeviceRotation rotation,
        DisplayProtocolProfile requestedProtocol,
        DisplayColorMode requestedColorMode,
        SerialPortOption? deviceInfo = null,
        CancellationToken cancellationToken = default)
    {
        var number = ParsePortNumber(portName);
        if (number <= 0)
            throw new ArgumentException("Expected a Windows COM port such as COM6.", nameof(portName));

        await _ioGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();

            await Task.Run(() =>
            {
                DisconnectCore();

                Exception? firstFailure = null;
                var protocol = ResolveProtocol(requestedProtocol, deviceInfo);
                var colorMode = ResolveColorMode(requestedColorMode);

                foreach (var baudRate in new[] { 115200, 921600 })
                {
                    TuringScreen? screen = null;
                    try
                    {
                        screen = new TuringScreen(
                            number,
                            baudRate,
                            MapProtocol(protocol),
                            MapColorMode(colorMode));

                        screen.Reset();
                        screen.InitializeComm();

                        // Some USB35INCHIPSV2 devices only identify themselves
                        // after HELLO. In Auto mode prefer the proven native-
                        // portrait software-rotation path for that sub-revision.
                        if (requestedProtocol == DisplayProtocolProfile.Auto &&
                            screen.DetectedModel.Contains("UsbMonitor 3.5", StringComparison.OrdinalIgnoreCase))
                        {
                            protocol = DisplayProtocolProfile.RevANativePortrait;
                            screen.ConfigureCompatibility(MapProtocol(protocol), MapColorMode(colorMode));
                        }

                        screen.ScreenOn();
                        screen.SetOrientation(MapOrientation(theme, rotation));
                        screen.SetBrightness(50);

                        _screen = screen;
                        ConnectedPort = portName;
                        ConnectedBaudRate = baudRate;
                        ConnectedModel = screen.DetectedModel;
                        ConnectedProtocol = protocol;
                        ConnectedColorMode = colorMode;
                        return;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        screen?.Dispose();
                        throw;
                    }
                    catch (Exception ex) when (baudRate == 115200 && IsBaudRateFailure(ex))
                    {
                        screen?.Dispose();
                        firstFailure = ex;
                    }
                    catch
                    {
                        screen?.Dispose();
                        throw;
                    }
                }

                throw new InvalidOperationException(
                    "The display port rejected both 115200 and 921600 baud.",
                    firstFailure);
            }, cancellationToken);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _ioGate.WaitAsync(cancellationToken);
        try
        {
            await Task.Run(DisconnectCore, cancellationToken);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task ApplyOrientationAsync(ThemeOrientation theme, DeviceRotation rotation, CancellationToken cancellationToken = default)
    {
        await _ioGate.WaitAsync(cancellationToken);
        try
        {
            var screen = _screen;
            if (screen is null) return;

            await Task.Run(() => screen.SetOrientation(MapOrientation(theme, rotation)), cancellationToken);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task DisplayAsync(SKBitmap bitmap, DeviceRotation rotation, CancellationToken cancellationToken = default)
    {
        await _ioGate.WaitAsync(cancellationToken);
        try
        {
            var screen = _screen;
            if (screen is null) return;

            await Task.Run(() => DisplayCore(screen, bitmap, rotation), cancellationToken);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task TestPatternAsync(CancellationToken cancellationToken = default)
    {
        await _ioGate.WaitAsync(cancellationToken);
        try
        {
            var screen = _screen ?? throw new InvalidOperationException("Connect the display first.");

            await Task.Run(() =>
            {
                var buffer = new ScreenBuffer(screen.Width, screen.Height);
                var bars = new[]
                {
                    ScreenBuffer.FullRgbToColor565(255, 0, 0),
                    ScreenBuffer.FullRgbToColor565(0, 255, 0),
                    ScreenBuffer.FullRgbToColor565(0, 0, 255),
                    ScreenBuffer.FullRgbToColor565(0, 255, 255),
                    ScreenBuffer.FullRgbToColor565(255, 0, 255),
                    ScreenBuffer.FullRgbToColor565(255, 255, 0)
                };
                var white = ScreenBuffer.FullRgbToColor565(255, 255, 255);
                var black = ScreenBuffer.FullRgbToColor565(0, 0, 0);

                for (var y = 0; y < screen.Height; y++)
                {
                    for (var x = 0; x < screen.Width; x++)
                    {
                        var index = Math.Min(bars.Length - 1, x * bars.Length / Math.Max(1, screen.Width));
                        var color = bars[index];

                        if (y < 8 || y >= screen.Height - 8 || x < 8 || x >= screen.Width - 8)
                            color = white;

                        if (y > screen.Height / 2 - 3 && y < screen.Height / 2 + 3)
                            color = black;

                        buffer[x, y] = color;
                    }
                }

                screen.ScreenOn();
                screen.SetBrightness(50);
                screen.DisplayBuffer(0, 0, buffer);
            }, cancellationToken);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task RunBenchmarkAsync(CancellationToken cancellationToken = default)
    {
        await _ioGate.WaitAsync(cancellationToken);
        try
        {
            var screen = _screen ?? throw new InvalidOperationException("Connect the display first.");
            await Task.Run(screen.RunBenchmark, cancellationToken);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    private static void DisplayCore(TuringScreen screen, SKBitmap bitmap, DeviceRotation rotation)
    {
        SKBitmap? rotated = null;
        var frame = bitmap;

        if (rotation != DeviceRotation.Degrees0)
        {
            rotated = Rotate(bitmap, rotation);
            frame = rotated;
        }

        try
        {
            if (frame.Width != screen.Width || frame.Height != screen.Height)
            {
                throw new InvalidOperationException(
                    $"Rendered frame is {frame.Width}x{frame.Height}, but the device expects {screen.Width}x{screen.Height}.");
            }

            var buffer = new ScreenBuffer(frame.Width, frame.Height);
            var pixels = frame.Pixels;

            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                var x = i % frame.Width;
                var y = i / frame.Width;
                buffer[x, y] = ScreenBuffer.FullRgbToColor565(p.Red, p.Green, p.Blue);
            }

            screen.DisplayBuffer(0, 0, buffer);
        }
        finally
        {
            rotated?.Dispose();
        }
    }

    private static SKBitmap Rotate(SKBitmap source, DeviceRotation rotation)
    {
        var swap = rotation is DeviceRotation.Degrees90 or DeviceRotation.Degrees270;
        var result = new SKBitmap(
            swap ? source.Height : source.Width,
            swap ? source.Width : source.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul);

        using var canvas = new SKCanvas(result);

        switch (rotation)
        {
            case DeviceRotation.Degrees90:
                canvas.Translate(result.Width, 0);
                canvas.RotateDegrees(90);
                break;
            case DeviceRotation.Degrees180:
                canvas.Translate(result.Width, result.Height);
                canvas.RotateDegrees(180);
                break;
            case DeviceRotation.Degrees270:
                canvas.Translate(0, result.Height);
                canvas.RotateDegrees(-90);
                break;
        }

        canvas.DrawBitmap(source, 0, 0);
        return result;
    }

    private void DisconnectCore()
    {
        var screen = _screen;
        _screen = null;
        ConnectedPort = null;
        ConnectedBaudRate = null;
        ConnectedModel = null;
        ConnectedProtocol = DisplayProtocolProfile.Auto;
        ConnectedColorMode = DisplayColorMode.Auto;
        try { screen?.Dispose(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisconnectCore();
        _ioGate.Dispose();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }


    private static DisplayProtocolProfile ResolveProtocol(
        DisplayProtocolProfile requested,
        SerialPortOption? deviceInfo)
    {
        if (requested != DisplayProtocolProfile.Auto)
            return requested;

        var metadata = string.Join(" ",
            deviceInfo?.FriendlyName ?? string.Empty,
            deviceInfo?.HardwareId ?? string.Empty,
            deviceInfo?.Manufacturer ?? string.Empty);

        if (metadata.Contains("USB35INCHIPSV2", StringComparison.OrdinalIgnoreCase) ||
            metadata.Contains("VID_1A86&PID_5722", StringComparison.OrdinalIgnoreCase) ||
            metadata.Contains("USBMONITOR", StringComparison.OrdinalIgnoreCase))
        {
            return DisplayProtocolProfile.RevANativePortrait;
        }

        return DisplayProtocolProfile.RevAHardwareLogical;
    }

    private static DisplayColorMode ResolveColorMode(DisplayColorMode requested)
        => requested == DisplayColorMode.Auto
            ? DisplayColorMode.Rgb565LittleEndian
            : requested;

    private static RevACompatibilityMode MapProtocol(DisplayProtocolProfile profile)
        => profile switch
        {
            DisplayProtocolProfile.RevANativePortrait => RevACompatibilityMode.NativePortraitSoftwareRotation,
            DisplayProtocolProfile.RevAHardwareNative => RevACompatibilityMode.HardwareNativeDimensions,
            _ => RevACompatibilityMode.HardwareLogicalDimensions
        };

    private static Rgb565Encoding MapColorMode(DisplayColorMode mode)
        => mode switch
        {
            DisplayColorMode.Bgr565LittleEndian => Rgb565Encoding.BgrLittleEndian,
            DisplayColorMode.Rgb565BigEndian => Rgb565Encoding.RgbBigEndian,
            DisplayColorMode.Bgr565BigEndian => Rgb565Encoding.BgrBigEndian,
            _ => Rgb565Encoding.RgbLittleEndian
        };

    private static bool IsBaudRateFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            var message = current.Message;
            if (message.Contains("baud", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("maximum", StringComparison.OrdinalIgnoreCase))
                return true;

            if (current.InnerException is null)
                break;
        }

        return exception is ArgumentOutOfRangeException;
    }

    private static int ParsePortNumber(string port)
        => int.TryParse(port.Replace("COM", "", StringComparison.OrdinalIgnoreCase), out var n) ? n : -1;

    private static TuringScreenOrientation MapOrientation(ThemeOrientation theme, DeviceRotation rotation)
    {
        return (theme, rotation) switch
        {
            (ThemeOrientation.Portrait, DeviceRotation.Degrees0) => TuringScreenOrientation.Portrait,
            (ThemeOrientation.Portrait, DeviceRotation.Degrees90) => TuringScreenOrientation.Landscape,
            (ThemeOrientation.Portrait, DeviceRotation.Degrees180) => TuringScreenOrientation.ReversePortrait,
            (ThemeOrientation.Portrait, DeviceRotation.Degrees270) => TuringScreenOrientation.ReverseLandscape,

            (ThemeOrientation.Landscape, DeviceRotation.Degrees0) => TuringScreenOrientation.Landscape,
            (ThemeOrientation.Landscape, DeviceRotation.Degrees90) => TuringScreenOrientation.ReversePortrait,
            (ThemeOrientation.Landscape, DeviceRotation.Degrees180) => TuringScreenOrientation.ReverseLandscape,
            (ThemeOrientation.Landscape, DeviceRotation.Degrees270) => TuringScreenOrientation.Portrait,

            _ => TuringScreenOrientation.Portrait
        };
    }
}
