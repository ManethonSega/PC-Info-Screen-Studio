using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;

namespace PCInfoScreenStudio.Controllers;

/// <summary>Owns discovery, connection, diagnostics and the latest-frame USB queue.</summary>
public sealed class DeviceController : ObservableObject, IDisposable
{
    private readonly IDisplayDevice _deviceService;
    private readonly Func<IReadOnlyList<SerialPortOption>> _discoverPorts;
    private readonly Func<ThemeDocument> _document;
    private readonly SettingsController _settings;
    private readonly object _frameQueueSync = new();
    private PendingDisplayFrame? _pendingFrame;
    private bool _frameSenderRunning;
    private bool _disposed;
    private string? _selectedPort;
    private string _deviceStatus = "Not connected";
    private bool _isDeviceBusy;
    private bool _livePreview;
    private DisplayProtocolProfile _displayProtocol;
    private DisplayColorMode _displayColorMode;
    private DateTimeOffset _suspendLiveDisplayUntil = DateTimeOffset.MinValue;

    public DeviceController(Func<ThemeDocument> document, SettingsController settings,
        IDisplayDevice? device = null, Func<IReadOnlyList<SerialPortOption>>? discoverPorts = null)
    {
        _document = document;
        _settings = settings;
        _deviceService = device ?? new DeviceService();
        _discoverPorts = discoverPorts ?? new SerialDeviceDiscoveryService().Discover;
        _selectedPort = settings.LastDisplayPort;
        _displayProtocol = settings.DisplayProtocol;
        _displayColorMode = settings.DisplayColorMode;
    }

    private ThemeDocument Document => _document();
    public ObservableCollection<SerialPortOption> Ports { get; } = [];
    public bool IsConnected => _deviceService.IsConnected;
    public string DisplayActionLabel => IsConnected ? "Stop display" : "Start display";

    public event EventHandler? LiveFrameRequested;
    public event EventHandler? CommandStatesChanged;
    public event EventHandler? ReconnectRequested;

    public string? SelectedPort
    {
        get => _selectedPort;
        set => SetProperty(ref _selectedPort, value);
    }

    public string DeviceStatus
    {
        get => _deviceStatus;
        set => SetProperty(ref _deviceStatus, value);
    }

    public bool IsDeviceBusy
    {
        get => _isDeviceBusy;
        private set
        {
            if (!SetProperty(ref _isDeviceBusy, value)) return;
            CommandStatesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool LivePreview
    {
        get => _livePreview;
        set
        {
            if (!SetProperty(ref _livePreview, value)) return;
            if (value) LiveFrameRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public DisplayProtocolProfile DisplayProtocol
    {
        get => _displayProtocol;
        set
        {
            if (!SetProperty(ref _displayProtocol, value)) return;
            _settings.DisplayProtocol = value;
            if (IsConnected && !IsDeviceBusy)
                _ = ApplyDisplayCompatibilityAsync();
        }
    }

    public DisplayColorMode DisplayColorMode
    {
        get => _displayColorMode;
        set
        {
            if (!SetProperty(ref _displayColorMode, value)) return;
            _settings.DisplayColorMode = value;
            if (IsConnected && !IsDeviceBusy)
                _ = ApplyDisplayCompatibilityAsync();
        }
    }

    public void RefreshPorts()
    {
        var previous = SelectedPort;
        Ports.Clear();

        foreach (var port in _discoverPorts())
            Ports.Add(port);

        var rememberedDevice = !string.IsNullOrWhiteSpace(_settings.LastDisplayHardwareId)
            ? Ports.FirstOrDefault(p => p.HardwareId.Equals(
                _settings.LastDisplayHardwareId, StringComparison.OrdinalIgnoreCase))
            : null;

        if (rememberedDevice is not null)
        {
            SelectedPort = rememberedDevice.PortName;
        }
        else if (!string.IsNullOrWhiteSpace(previous) &&
            Ports.Any(p => p.PortName.Equals(previous, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedPort = previous;
        }
        else
        {
            SelectedPort = Ports.FirstOrDefault(p => p.IsLikelyScreen)?.PortName
                ?? Ports.FirstOrDefault()?.PortName;
        }
    }

    public void DetectScreen()
    {
        RefreshPorts();

        var candidate = Ports.FirstOrDefault(p => p.IsLikelyScreen);
        if (candidate is not null)
        {
            SelectedPort = candidate.PortName;
            DeviceStatus = $"Likely screen detected: {candidate.DisplayName}";
            return;
        }

        if (Ports.Count == 1)
        {
            SelectedPort = Ports[0].PortName;
            DeviceStatus = $"One serial device found: {Ports[0].DisplayName}";
            return;
        }

        DeviceStatus = Ports.Count == 0
            ? "No serial screen/COM device detected."
            : "No screen could be identified automatically. Choose the USB serial device from the list.";
    }

    public async Task ConnectOrDisconnectAsync(bool automatic = false)
    {
        if (_disposed || IsDeviceBusy) return;

        if (_deviceService.IsConnected)
        {
            IsDeviceBusy = true;
            DeviceStatus = "Stopping display...";
            LivePreview = false;
            try
            {
                await _deviceService.DisconnectAsync();
                DeviceStatus = "Display stopped";
            }
            catch (Exception ex)
            {
                DeviceStatus = "Disconnect error: " + ex.Message;
            }
            finally
            {
                IsDeviceBusy = false;
                RaisePropertyChanged(nameof(DisplayActionLabel));
                CommandStatesChanged?.Invoke(this, EventArgs.Empty);
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedPort))
            DetectScreen();

        if (string.IsNullOrWhiteSpace(SelectedPort))
        {
            DeviceStatus = "No USB display was found. Connect the cable and the app will check again automatically.";
            return;
        }

        IsDeviceBusy = true;
        DeviceStatus = $"Connecting to {SelectedPort}...";
        try
        {
            var deviceInfo = Ports.FirstOrDefault(p =>
                p.PortName.Equals(SelectedPort, StringComparison.OrdinalIgnoreCase));

            await _deviceService.ConnectAsync(
                SelectedPort,
                Document.Orientation,
                Document.DeviceRotation,
                DisplayProtocol,
                DisplayColorMode,
                deviceInfo);

            var connectedPort = _deviceService.ConnectedPort ?? SelectedPort;
            SelectedPort = connectedPort;
            var connectedInfo = _discoverPorts().FirstOrDefault(p =>
                p.PortName.Equals(connectedPort, StringComparison.OrdinalIgnoreCase)) ?? deviceInfo;
            _settings.LastDisplayPort = connectedPort;
            if (!string.IsNullOrWhiteSpace(connectedInfo?.HardwareId))
                _settings.LastDisplayHardwareId = connectedInfo.HardwareId;
            if (!string.IsNullOrWhiteSpace(connectedInfo?.FriendlyName))
                _settings.LastDisplayFriendlyName = connectedInfo.FriendlyName;
            LivePreview = true;
            DeviceStatus = BuildConnectionStatus();
            RaisePropertyChanged(nameof(DisplayActionLabel));
            LiveFrameRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (UnauthorizedAccessException)
        {
            await ReleaseFailedConnectionAsync();
            DeviceStatus = _settings.AutoStartDisplay
                ? $"{SelectedPort} is busy. Close other screen software; the app will retry automatically."
                : $"{SelectedPort} is busy. Close other screen software, then press Start display.";
        }
        catch (Exception ex)
        {
            await ReleaseFailedConnectionAsync();
            DeviceStatus = FriendlyConnectionError(ex, SelectedPort);
        }
        finally
        {
            IsDeviceBusy = false;
            RaisePropertyChanged(nameof(DisplayActionLabel));
            CommandStatesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task HandleDeviceChangeAsync()
    {
        if (_disposed) return;

        RefreshPorts();
        var connectedPort = _deviceService.ConnectedPort;
        if (_deviceService.IsConnected && !string.IsNullOrWhiteSpace(connectedPort) &&
            Ports.All(p => !p.PortName.Equals(connectedPort, StringComparison.OrdinalIgnoreCase)))
        {
            await ReleaseFailedConnectionAsync();
            DeviceStatus = "Display disconnected. Waiting for it to be reconnected...";
            ReconnectRequested?.Invoke(this, EventArgs.Empty);
            RaisePropertyChanged(nameof(DisplayActionLabel));
            CommandStatesChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!_deviceService.IsConnected && Ports.Count > 0)
        {
            DeviceStatus = "USB display change detected. Checking for the screen...";
            ReconnectRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task ClearDisplayAsync()
    {
        if (_disposed || !_deviceService.IsConnected) return;

        try
        {
            DeviceStatus = "Clearing display...";
            await _deviceService.ClearAsync();
        }
        catch
        {
            // Windows may stop USB devices before the app receives its shutdown event.
            // Clearing the display is therefore intentionally best-effort.
        }
    }

    private async Task ReleaseFailedConnectionAsync()
    {
        try { await _deviceService.DisconnectAsync(); }
        catch { }
    }

    private string FriendlyConnectionError(Exception error, string? port)
    {
        var message = error.GetBaseException().Message;
        var location = string.IsNullOrWhiteSpace(port) ? "the selected USB port" : port;
        var nextStep = _settings.AutoStartDisplay
            ? "The app will retry automatically."
            : "Check the USB cable, then press Start display.";
        if (message.Contains("semaphore timeout", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("did not respond", StringComparison.OrdinalIgnoreCase))
        {
            return $"The display on {location} did not respond. {nextStep}";
        }

        if (error is IOException || message.Contains("I/O", StringComparison.OrdinalIgnoreCase))
            return $"The display on {location} is not ready. {nextStep}";

        return $"Could not connect to the display on {location}. {nextStep}";
    }

    public async Task TestScreenAsync()
    {
        if (IsDeviceBusy) return;

        IsDeviceBusy = true;
        _suspendLiveDisplayUntil = DateTimeOffset.UtcNow.AddSeconds(8);
        DeviceStatus = "Sending display color test...";
        try
        {
            await _deviceService.TestPatternAsync();
            DeviceStatus = "Color test visible for 8 seconds. Expected: red · green · blue · cyan · magenta · yellow.";
            _ = ResumeLiveDisplayAfterDiagnosticAsync();
        }
        catch (Exception ex)
        {
            DeviceStatus = "Screen test failed";
            MessageBox.Show(ex.Message, "Screen test failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsDeviceBusy = false;
        }
    }

    private async Task ResumeLiveDisplayAfterDiagnosticAsync()
    {
        try
        {
            var delay = _suspendLiveDisplayUntil - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay);

            if (!_disposed && LivePreview && _deviceService.IsConnected)
                await Application.Current.Dispatcher.InvokeAsync(() => LiveFrameRequested?.Invoke(this, EventArgs.Empty));
        }
        catch
        {
            // Diagnostic display restoration is best-effort.
        }
    }

    public async Task ApplyDisplayCompatibilityAsync()
    {
        if (!_deviceService.IsConnected || IsDeviceBusy)
            return;

        IsDeviceBusy = true;
        DeviceStatus = "Applying display compatibility settings...";
        try
        {
            var deviceInfo = Ports.FirstOrDefault(p =>
                string.Equals(p.PortName, _deviceService.ConnectedPort, StringComparison.OrdinalIgnoreCase));

            await _deviceService.ApplyCompatibilityAsync(
                DisplayProtocol,
                DisplayColorMode,
                deviceInfo,
                Document.Orientation,
                Document.DeviceRotation);

            DeviceStatus = BuildConnectionStatus();

            if (LivePreview)
                LiveFrameRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            DeviceStatus = "Display compatibility error: " + ex.Message;
        }
        finally
        {
            IsDeviceBusy = false;
        }
    }

    public async Task ApplyDeviceOrientationAsync()
    {
        try
        {
            await _deviceService.ApplyOrientationAsync(Document.Orientation, Document.DeviceRotation);
            if (LivePreview)
                LiveFrameRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            DeviceStatus = "Rotation error: " + ex.Message;
        }
    }

    private string BuildConnectionStatus()
        => $"Connected: {_deviceService.ConnectedModel ?? "screen"} on {_deviceService.ConnectedPort} " +
           $"@ {_deviceService.ConnectedBaudRate ?? 0} baud · {_deviceService.ConnectedProtocol} · {_deviceService.ConnectedColorMode}";

    public async Task RunBenchmarkAsync()
    {
        if (IsDeviceBusy) return;

        IsDeviceBusy = true;
        DeviceStatus = "Benchmarking display...";
        try
        {
            await _deviceService.RunBenchmarkAsync();
            DeviceStatus = $"Connected: {_deviceService.ConnectedPort}";
            MessageBox.Show("The driver benchmark completed.", "Benchmark", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            DeviceStatus = "Benchmark failed";
            MessageBox.Show(ex.Message, "Benchmark failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsDeviceBusy = false;
        }
    }

    public void SendLiveFrame(SkiaSharp.SKBitmap bitmap, bool force = false)
    {
        if (_disposed || (!LivePreview && !force) || !_deviceService.IsConnected) return;
        if (DateTimeOffset.UtcNow < _suspendLiveDisplayUntil) return;

        var next = new PendingDisplayFrame(bitmap.Copy(), Document.DeviceRotation);
        lock (_frameQueueSync)
        {
            if (_disposed)
            {
                next.Bitmap.Dispose();
                return;
            }

            // USB transmission is slower than preview rendering. Keep only the
            // newest waiting frame so the display never builds up animation lag.
            _pendingFrame?.Bitmap.Dispose();
            _pendingFrame = next;

            if (_frameSenderRunning)
                return;

            _frameSenderRunning = true;
        }

        _ = DrainLiveFrameQueueAsync();
    }

    private async Task DrainLiveFrameQueueAsync()
    {
        while (true)
        {
            PendingDisplayFrame? frame;
            lock (_frameQueueSync)
            {
                frame = _pendingFrame;
                _pendingFrame = null;

                if (frame is null)
                {
                    _frameSenderRunning = false;
                    return;
                }
            }

            try
            {
                await _deviceService.DisplayAsync(frame.Bitmap, frame.Rotation);
                if (_disposed || Application.Current.Dispatcher.HasShutdownStarted) return;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    DeviceStatus = BuildConnectionStatus());
            }
            catch (Exception ex)
            {
                if (_disposed || Application.Current.Dispatcher.HasShutdownStarted) return;
                await ReleaseFailedConnectionAsync();
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    DeviceStatus = FriendlyConnectionError(ex, _settings.LastDisplayPort);
                    RaisePropertyChanged(nameof(DisplayActionLabel));
                    CommandStatesChanged?.Invoke(this, EventArgs.Empty);
                    ReconnectRequested?.Invoke(this, EventArgs.Empty);
                });
                lock (_frameQueueSync)
                {
                    _pendingFrame?.Bitmap.Dispose();
                    _pendingFrame = null;
                    _frameSenderRunning = false;
                }
                return;
            }
            finally
            {
                frame.Bitmap.Dispose();
            }
        }
    }


    private sealed record PendingDisplayFrame(SkiaSharp.SKBitmap Bitmap, DeviceRotation Rotation);

    public void Dispose()
    {
        lock (_frameQueueSync)
        {
            if (_disposed) return;
            _disposed = true;
            _pendingFrame?.Bitmap.Dispose();
            _pendingFrame = null;
        }
        _deviceService.Dispose();
    }
}
