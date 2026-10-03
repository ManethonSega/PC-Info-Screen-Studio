using System.IO.Ports;
using SkiaSharp;
using Tedd.TuringScreen;
using PCInfoScreenStudio.Models;
using TuringScreenOrientation = Tedd.TuringScreen.ScreenOrientation;

namespace PCInfoScreenStudio.Services;

public sealed class DeviceService : IDisposable
{
    private TuringScreen? _screen;

    public bool IsConnected => _screen is not null;
    public string? ConnectedPort { get; private set; }

    public IReadOnlyList<string> GetPorts()
        => SerialPort.GetPortNames().OrderBy(ParsePortNumber).ToArray();

    public void Connect(string portName, ThemeDocument document)
    {
        Disconnect();

        var number = ParsePortNumber(portName);
        if (number <= 0)
            throw new ArgumentException("Expected a Windows COM port such as COM6.", nameof(portName));

        _screen = new TuringScreen(number);
        ConnectedPort = portName;
        ApplyOrientation(document);
        _screen.SetBrightness(100);
    }

    public void ApplyOrientation(ThemeDocument document)
    {
        if (_screen is null) return;
        _screen.SetOrientation(MapOrientation(document.Orientation, document.DeviceRotation));
    }

    public void Display(SKBitmap bitmap, ThemeDocument document)
    {
        if (_screen is null) return;

        SKBitmap? rotated = null;
        var frame = bitmap;
        if (document.DeviceRotation != DeviceRotation.Degrees0)
        {
            rotated = Rotate(bitmap, document.DeviceRotation);
            frame = rotated;
        }

        try
        {
            if (frame.Width != _screen.Width || frame.Height != _screen.Height)
            {
                throw new InvalidOperationException(
                    $"Rendered frame is {frame.Width}x{frame.Height}, but the device expects {_screen.Width}x{_screen.Height}.");
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

            _screen.DisplayBuffer(0, 0, buffer);
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

    public void SetBrightness(int percent) => _screen?.SetBrightness(percent);
    public void RunBenchmark() => _screen?.RunBenchmark();

    public void Disconnect()
    {
        _screen?.Dispose();
        _screen = null;
        ConnectedPort = null;
    }

    public void Dispose() => Disconnect();

    private static int ParsePortNumber(string port)
        => int.TryParse(port.Replace("COM", "", StringComparison.OrdinalIgnoreCase), out var n) ? n : -1;

    private static TuringScreenOrientation MapOrientation(ThemeOrientation theme, DeviceRotation rotation)
    {
        var startsLandscape = theme == ThemeOrientation.Landscape;
        var quarterTurn = rotation is DeviceRotation.Degrees90 or DeviceRotation.Degrees270;
        var finalLandscape = quarterTurn ? !startsLandscape : startsLandscape;

        // Rotation itself is applied to the rendered bitmap. The driver only needs
        // the logical dimensions/memory mapping, so normal Portrait/Landscape is enough.
        return finalLandscape
            ? TuringScreenOrientation.Landscape
            : TuringScreenOrientation.Portrait;
    }
}
