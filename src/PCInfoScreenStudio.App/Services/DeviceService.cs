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

    public IReadOnlyList<string> GetPorts()
        => SerialPort.GetPortNames().OrderBy(ParsePortNumber).ToArray();

    public async Task ConnectAsync(string portName, ThemeOrientation theme, DeviceRotation rotation, CancellationToken cancellationToken = default)
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

                var screen = new TuringScreen(number);
                try
                {
                    screen.SetOrientation(MapOrientation(theme, rotation));
                    screen.SetBrightness(100);
                    _screen = screen;
                    ConnectedPort = portName;
                }
                catch
                {
                    screen.Dispose();
                    throw;
                }
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

    private static int ParsePortNumber(string port)
        => int.TryParse(port.Replace("COM", "", StringComparison.OrdinalIgnoreCase), out var n) ? n : -1;

    private static TuringScreenOrientation MapOrientation(ThemeOrientation theme, DeviceRotation rotation)
    {
        var startsLandscape = theme == ThemeOrientation.Landscape;
        var quarterTurn = rotation is DeviceRotation.Degrees90 or DeviceRotation.Degrees270;
        var finalLandscape = quarterTurn ? !startsLandscape : startsLandscape;

        return finalLandscape
            ? TuringScreenOrientation.Landscape
            : TuringScreenOrientation.Portrait;
    }
}
