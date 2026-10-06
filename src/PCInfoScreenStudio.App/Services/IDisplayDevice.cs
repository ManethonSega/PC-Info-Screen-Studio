using PCInfoScreenStudio.Models;
using SkiaSharp;

namespace PCInfoScreenStudio.Services;

/// <summary>USB operations consumed by the controller, separate from serial protocol implementation.</summary>
public interface IDisplayDevice : IDisposable
{
    bool IsConnected { get; }
    string? ConnectedPort { get; }
    int? ConnectedBaudRate { get; }
    string? ConnectedModel { get; }
    DisplayProtocolProfile ConnectedProtocol { get; }
    DisplayColorMode ConnectedColorMode { get; }

    Task ConnectAsync(string portName, ThemeOrientation theme, DeviceRotation rotation,
        DisplayProtocolProfile requestedProtocol, DisplayColorMode requestedColorMode,
        SerialPortOption? deviceInfo = null, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task ApplyCompatibilityAsync(DisplayProtocolProfile requestedProtocol, DisplayColorMode requestedColorMode,
        SerialPortOption? deviceInfo, ThemeOrientation theme, DeviceRotation rotation,
        CancellationToken cancellationToken = default);
    Task ApplyOrientationAsync(ThemeOrientation theme, DeviceRotation rotation,
        CancellationToken cancellationToken = default);
    Task DisplayAsync(SKBitmap bitmap, DeviceRotation rotation, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
    Task TestPatternAsync(CancellationToken cancellationToken = default);
    Task RunBenchmarkAsync(CancellationToken cancellationToken = default);
}
