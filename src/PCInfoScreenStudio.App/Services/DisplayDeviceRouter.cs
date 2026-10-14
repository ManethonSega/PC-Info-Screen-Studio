using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services.TuringUsb;
using SkiaSharp;

namespace PCInfoScreenStudio.Services;

/// <summary>Routes a selected physical device to its serial or WinUSB transport.</summary>
public sealed class DisplayDeviceRouter : IDisplayDevice
{
    private readonly IDisplayDevice _serial;
    private readonly IDisplayDevice _usb;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IDisplayDevice _active;
    private bool _disposed;

    public DisplayDeviceRouter(IDisplayDevice? serial = null, IDisplayDevice? usb = null)
    {
        _serial = serial ?? new DeviceService();
        _usb = usb ?? new TuringUsbDisplayDevice();
        _active = _serial;
    }
    public bool IsConnected => _active.IsConnected;
    public string? ConnectedPort => _active.ConnectedPort;
    public int? ConnectedBaudRate => _active.ConnectedBaudRate;
    public string? ConnectedModel => _active.ConnectedModel;
    public DisplayProtocolProfile ConnectedProtocol => _active.ConnectedProtocol;
    public DisplayColorMode ConnectedColorMode => _active.ConnectedColorMode;

    public Task ConnectAsync(string portName, ThemeOrientation theme, DeviceRotation rotation,
        DisplayProtocolProfile requestedProtocol, DisplayColorMode requestedColorMode,
        SerialPortOption? deviceInfo = null, CancellationToken cancellationToken = default, int brightness = 50)
        => RunAsync(async () =>
        {
            var next = TuringUsbProtocol.IsDevice(portName) ? _usb : _serial;
            if (requestedProtocol == DisplayProtocolProfile.TuringUsb52 && next == _serial)
                throw new DisplaySetupException("Select the TURZX 5.2-inch USB device from the device list. It does not use a COM port.");
            if (next != _active) await _active.DisconnectAsync(cancellationToken);
            _active = next;
            await _active.ConnectAsync(portName, theme, rotation, requestedProtocol, requestedColorMode, deviceInfo, cancellationToken, brightness);
        }, cancellationToken);
    public Task SetBrightnessAsync(int level, CancellationToken cancellationToken = default)
        => RunAsync(() => _active.SetBrightnessAsync(level, cancellationToken), cancellationToken);
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
        => RunAsync(() => _active.DisconnectAsync(cancellationToken), cancellationToken);
    public Task ApplyCompatibilityAsync(DisplayProtocolProfile requestedProtocol, DisplayColorMode requestedColorMode,
        SerialPortOption? deviceInfo, ThemeOrientation theme, DeviceRotation rotation, CancellationToken cancellationToken = default)
        => RunAsync(() => _active.ApplyCompatibilityAsync(requestedProtocol, requestedColorMode, deviceInfo, theme, rotation, cancellationToken), cancellationToken);
    public Task ApplyOrientationAsync(ThemeOrientation theme, DeviceRotation rotation, CancellationToken cancellationToken = default)
        => RunAsync(() => _active.ApplyOrientationAsync(theme, rotation, cancellationToken), cancellationToken);
    public Task DisplayAsync(SKBitmap bitmap, DeviceRotation rotation, CancellationToken cancellationToken = default)
        => RunAsync(() => _active.DisplayAsync(bitmap, rotation, cancellationToken), cancellationToken);
    public Task ClearAsync(CancellationToken cancellationToken = default)
        => RunAsync(() => _active.ClearAsync(cancellationToken), cancellationToken);
    public Task TestPatternAsync(CancellationToken cancellationToken = default)
        => RunAsync(() => _active.TestPatternAsync(cancellationToken), cancellationToken);
    public Task RunBenchmarkAsync(CancellationToken cancellationToken = default)
        => RunAsync(() => _active.RunBenchmarkAsync(cancellationToken), cancellationToken);

    private async Task RunAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { ObjectDisposedException.ThrowIf(_disposed, this); await operation().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
    public void Dispose()
    {
        _gate.Wait();
        try { if (_disposed) return; _disposed = true; _serial.Dispose(); _usb.Dispose(); }
        finally { _gate.Release(); }
    }
}

public sealed class DisplayDeviceDiscoveryService
{
    public IReadOnlyList<SerialPortOption> Discover()
        => WinUsbTransport.Discover().Concat(new SerialDeviceDiscoveryService().Discover()).ToArray();
}
