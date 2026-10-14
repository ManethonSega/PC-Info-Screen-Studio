using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;
using PCInfoScreenStudio.Services.TuringUsb;

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
    private long _sentFrames;
    private long _droppedFrames;
    private double _averageTransferMilliseconds;
    public double AverageTransferMilliseconds => Volatile.Read(ref _averageTransferMilliseconds);
    public object GetDiagnostics() => new { selectedFamily = PreferredCanvasSize.ToString(), connected = IsConnected, protocol = DisplayProtocol.ToString(), connectedProtocol = _deviceService.ConnectedProtocol.ToString(), transport = PreferredCanvasSize == DisplayCanvasSize.Turing52 ? "WinUSB JPEG" : "USB-CDC RGB565", usbId = GetUsbId(), colourMode = DisplayColorMode.ToString(), baudRate = _deviceService.ConnectedBaudRate, sentFrames = Interlocked.Read(ref _sentFrames), replacedFrames = Interlocked.Read(ref _droppedFrames), averageTransferMilliseconds = AverageTransferMilliseconds };
    private bool _refreshingPorts;
    private long _connectionGeneration;
    private string? _selectedPort;
    private string _deviceStatus = "Not connected";
    private bool _isDeviceBusy;
    private bool _livePreview;
    private DisplayProtocolProfile _displayProtocol;
    private DisplayColorMode _displayColorMode;
    private DateTimeOffset _suspendLiveDisplayUntil = DateTimeOffset.MinValue;

    private string? GetUsbId()
    {
        var id = Ports.FirstOrDefault(p => p.PortName == SelectedPort)?.HardwareId ?? _settings.LastDisplayHardwareId ?? string.Empty;
        var match = System.Text.RegularExpressions.Regex.Match(id, @"VID_[0-9A-F]{4}&PID_[0-9A-F]{4}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }

    public DeviceController(Func<ThemeDocument> document, SettingsController settings,
        IDisplayDevice? device = null, Func<IReadOnlyList<SerialPortOption>>? discoverPorts = null)
    {
        _document = document;
        _settings = settings;
        _deviceService = device ?? new DisplayDeviceRouter();
        _discoverPorts = discoverPorts ?? new DisplayDeviceDiscoveryService().Discover;
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
        set
        {
            // WPF clears SelectedValue when discovery updates the list. That is
            // not a user's choice and must not change the reconnect target.
            if (_refreshingPorts || string.IsNullOrWhiteSpace(value)) return;
            if (_selectedPort == value) return;
            RememberSelection(value);
            SetProperty(ref _selectedPort, value);
        }
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

    public DisplayCanvasSize PreferredCanvasSize => _settings.PreferredCanvasSize
        ?? (TuringUsbProtocol.IsDevice(_settings.LastDisplayPort)
            ? DisplayCanvasSize.Turing52 : DisplayCanvasSize.Serial35);

    private bool MatchesScreen(SerialPortOption port)
        => TuringUsbProtocol.IsDevice(port.PortName) == (PreferredCanvasSize == DisplayCanvasSize.Turing52);

    private void RememberSelection(string port)
    {
        var info = Ports.FirstOrDefault(p => p.PortName.Equals(port, StringComparison.OrdinalIgnoreCase));
        _settings.LastDisplayPort = port;
        _settings.LastDisplayHardwareId = info?.HardwareId;
        _settings.LastDisplayFriendlyName = info?.FriendlyName;
        _settings.PreferredCanvasSize = TuringUsbProtocol.IsDevice(port)
            ? DisplayCanvasSize.Turing52 : DisplayCanvasSize.Serial35;
    }

    public SerialPortOption? FindPreferredDisplay()
    {
        var candidates = Ports.Where(MatchesScreen).ToArray();
        if (!string.IsNullOrWhiteSpace(_settings.LastDisplayHardwareId))
            return candidates.FirstOrDefault(p => p.HardwareId.Equals(
                _settings.LastDisplayHardwareId, StringComparison.OrdinalIgnoreCase));
        return candidates.FirstOrDefault(p => p.PortName.Equals(SelectedPort, StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(p => p.IsLikelyScreen)
            ?? (candidates.Length == 1 ? candidates[0] : null);
    }

    public void RefreshPorts()
    {
        var discovered = _discoverPorts();
        _refreshingPorts = true;
        try
        {
            // Preserve item identity and selection during unchanged USB notifications.
            foreach (var old in Ports.Where(p => !discovered.Contains(p)).ToArray()) Ports.Remove(old);
            foreach (var port in discovered.Where(p => !Ports.Contains(p))) Ports.Add(port);
            var preferred = FindPreferredDisplay();
            if (preferred is not null) SetProperty(ref _selectedPort, preferred.PortName, nameof(SelectedPort));
        }
        finally { _refreshingPorts = false; }
    }

    public void DetectScreen()
    {
        RefreshPorts();
        var candidate = FindPreferredDisplay();
        if (candidate is not null)
        {
            SelectedPort = candidate.PortName;
            DeviceStatus = $"Screen detected: {candidate.DisplayName}";
        }
        else DeviceStatus = "The selected screen is not available. Connect it or choose another screen in the top bar.";
    }

    public async Task SelectScreenAsync(DisplayCanvasSize size)
    {
        if (_disposed || IsDeviceBusy) return;
        var resume = IsConnected || _settings.AutoStartDisplay;
        IsDeviceBusy = true;
        LivePreview = false;
        InvalidatePendingFrames();
        try
        {
            await _deviceService.DisconnectAsync();
            _settings.PreferredCanvasSize = size;
            _settings.LastDisplayHardwareId = null;
            _settings.LastDisplayPort = null;
            _settings.LastDisplayFriendlyName = null;
            // A protocol override for the other family must not block selection.
            DisplayProtocol = DisplayProtocolProfile.Auto;
            _selectedPort = null;
            RefreshPorts();
            if (SelectedPort is not null) RememberSelection(SelectedPort);
            RaisePropertyChanged(nameof(SelectedPort));
            DeviceStatus = "Screen selected. Waiting for the display...";
        }
        catch (Exception ex) { DeviceStatus = FriendlyConnectionError(ex, SelectedPort); }
        finally
        {
            IsDeviceBusy = false;
            RaisePropertyChanged(nameof(DisplayActionLabel));
        }
        if (resume) await ConnectOrDisconnectAsync(automatic: true);
    }

    private void InvalidatePendingFrames()
    {
        lock (_frameQueueSync)
        {
            _connectionGeneration++;
            Volatile.Write(ref _averageTransferMilliseconds, 0);
            _pendingFrame?.Bitmap.Dispose();
            _pendingFrame = null;
        }
    }

    public async Task ConnectOrDisconnectAsync(bool automatic = false)
    {
        if (_disposed || IsDeviceBusy) return;

        if (_deviceService.IsConnected)
        {
            IsDeviceBusy = true;
            DeviceStatus = "Stopping display...";
            LivePreview = false;
            InvalidatePendingFrames();
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

        if (string.IsNullOrWhiteSpace(SelectedPort) || !Ports.Any(p => p.PortName == SelectedPort && MatchesScreen(p)))
            DetectScreen();

        if (string.IsNullOrWhiteSpace(SelectedPort) || !Ports.Any(p => p.PortName == SelectedPort && MatchesScreen(p)))
        {
            DeviceStatus = "No USB display was found. Connect the cable and the app will check again automatically.";
            return;
        }

        IsDeviceBusy = true;
        var selectedLabel = Ports.FirstOrDefault(p => p.PortName == SelectedPort)?.DisplayName ?? SelectedPort;
        DeviceStatus = $"Connecting to {selectedLabel}...";
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

            InvalidatePendingFrames();
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
                ? $"{selectedLabel} is busy. Close other screen software; the app will retry automatically."
                : $"{SelectedPort} is busy. Close other screen software, then press Start display.";
        }
        catch (Exception ex)
        {
            DiagnosticsService.RecordError("Display operation", ex);
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
        LivePreview = false;
        InvalidatePendingFrames();
        try { await _deviceService.DisconnectAsync(); }
        catch { }
    }

    private string FriendlyConnectionError(Exception error, string? port)
    {
        if (error is DisplaySetupException) return error.Message;
        var message = error.GetBaseException().Message;
        var location = TuringUsbProtocol.IsDevice(port) ? "the TURZX USB connection"
            : string.IsNullOrWhiteSpace(port) ? "the selected USB port" : port;
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
            DiagnosticsService.RecordError("Display operation", ex);
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
            DiagnosticsService.RecordError("Display operation", ex);
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
            DiagnosticsService.RecordError("Display operation", ex);
            DeviceStatus = "Rotation error: " + ex.Message;
        }
    }

    private string BuildConnectionStatus()
        => _deviceService.ConnectedProtocol == DisplayProtocolProfile.TuringUsb52
            ? "Connected: TURZX 5.2-inch USB · compressed images · experimental"
            : $"Connected: {_deviceService.ConnectedModel ?? "screen"} on {_deviceService.ConnectedPort} " +
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
            DiagnosticsService.RecordError("Display operation", ex);
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

        var next = new PendingDisplayFrame(bitmap.Copy(), Document.DeviceRotation, Document.Mode, _connectionGeneration);
        lock (_frameQueueSync)
        {
            if (_disposed)
            {
                next.Bitmap.Dispose();
                return;
            }

            if (_pendingFrame is not null) Interlocked.Increment(ref _droppedFrames);
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
                if (frame.Generation != _connectionGeneration || !_deviceService.IsConnected || frame.Mode != Document.Mode) continue;
                var transferStart = System.Diagnostics.Stopwatch.GetTimestamp();
                await _deviceService.DisplayAsync(frame.Bitmap, frame.Rotation);
                Interlocked.Increment(ref _sentFrames);
                var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(transferStart).TotalMilliseconds;
                var previous = AverageTransferMilliseconds;
                Volatile.Write(ref _averageTransferMilliseconds, previous == 0 ? elapsed : previous * .8 + elapsed * .2);
                if (frame.Generation != _connectionGeneration) continue;
                if (_disposed || Application.Current.Dispatcher.HasShutdownStarted) return;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    DeviceStatus = BuildConnectionStatus());
            }
            catch (Exception ex)
            {
                if (frame.Generation != _connectionGeneration) continue;
                if (_disposed || Application.Current.Dispatcher.HasShutdownStarted) return;
                DiagnosticsService.RecordError("Display transfer", ex);
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


    public void NotifyModeChanged() => InvalidatePendingFrames();

    private sealed record PendingDisplayFrame(SkiaSharp.SKBitmap Bitmap, DeviceRotation Rotation, ScreenMode Mode, long Generation);

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
