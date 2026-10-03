using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;

namespace PCInfoScreenStudio.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ThemePackageService _packageService = new();
    private readonly AssetImportService _assetService = new();
    private readonly FileDialogService _dialogs = new();
    private readonly DeviceService _deviceService = new();
    private readonly SerialDeviceDiscoveryService _serialDiscovery = new();
    private readonly ThemeLibraryService _themeLibrary = new();
    private readonly SystemMetricsService _systemMetrics = new();
    private readonly HardwareMetricsService _hardwareMetrics = new();
    private readonly WeatherMetricsService _weatherMetrics = new();
    private readonly AppSettingsService _settingsService = new();
    private readonly AppSettings _appSettings;
    private readonly DispatcherTimer _dataTimer;
    private readonly DispatcherTimer _animationTimer;
    private ThemeWorkspace _workspace;
    private WidgetModel? _selectedWidget;
    private string? _selectedPort;
    private ThemeLibraryItem? _selectedTheme;
    private string _deviceStatus = "Not connected";
    private string _weatherCity = string.Empty;
    private string _weatherStatus = "Weather city not configured.";
    private string _hardwareStatus = "Hardware sensors not initialized.";
    private DisplayProtocolProfile _displayProtocol;
    private DisplayColorMode _displayColorMode;
    private bool _livePreview;
    private bool _useLiveData;
    private bool _suppressDirty;
    private bool _isDeviceBusy;
    private int _frameSendBusy;
    private int _dataSampleBusy;
    private DateTimeOffset _suspendLiveDisplayUntil = DateTimeOffset.MinValue;

    public MainViewModel()
    {
        _appSettings = _settingsService.Load();
        _weatherCity = _appSettings.WeatherCity;
        _displayProtocol = _appSettings.DisplayProtocol;
        _displayColorMode = _appSettings.DisplayColorMode;
        _workspace = _packageService.CreateNewWorkspace();
        Ports = [];
        Themes = [];
        FontAssets = [];

        NewCommand = new RelayCommand(NewTheme);
        OpenCommand = new RelayCommand(async () => await OpenThemeAsync());
        SaveCommand = new RelayCommand(async () => await SaveAsync(false));
        SaveAsCommand = new RelayCommand(async () => await SaveAsync(true));
        AddWidgetCommand = new RelayCommand(AddWidget);
        DeleteWidgetCommand = new RelayCommand(DeleteSelected, () => SelectedWidget is not null);
        DuplicateWidgetCommand = new RelayCommand(DuplicateSelected, () => SelectedWidget is not null);
        MoveLayerUpCommand = new RelayCommand(() => MoveLayer(1), () => SelectedWidget is not null);
        MoveLayerDownCommand = new RelayCommand(() => MoveLayer(-1), () => SelectedWidget is not null);
        ImportImageCommand = new RelayCommand(() => ImportMedia(ThemeAssetKind.Image));
        ImportGifCommand = new RelayCommand(() => ImportMedia(ThemeAssetKind.Gif));
        ImportVideoCommand = new RelayCommand(() => ImportMedia(ThemeAssetKind.Video));
        ImportFontCommand = new RelayCommand(ImportFont);
        ToggleOrientationCommand = new RelayCommand(ToggleOrientation);
        RotateDeviceCommand = new RelayCommand(RotateDevice);
        RefreshPortsCommand = new RelayCommand(RefreshPorts);
        DetectScreenCommand = new RelayCommand(DetectScreen, () => !IsDeviceBusy);
        ConnectCommand = new RelayCommand(() => _ = ConnectOrDisconnectAsync(), () => !IsDeviceBusy);
        TestScreenCommand = new RelayCommand(() => _ = TestScreenAsync(), () => _deviceService.IsConnected && !IsDeviceBusy);
        BenchmarkCommand = new RelayCommand(() => _ = RunBenchmarkAsync(), () => _deviceService.IsConnected && !IsDeviceBusy);
        RefreshThemesCommand = new RelayCommand(RefreshThemes);
        LoadThemeCommand = new RelayCommand(() => _ = LoadSelectedThemeAsync(), () => SelectedTheme is not null);
        UpdateWeatherCommand = new RelayCommand(() => _ = UpdateWeatherAsync());
        RestartElevatedCommand = new RelayCommand(() => RestartElevated());
        EnableFullSensorsCommand = new RelayCommand(() => _ = EnableFullSensorsAsync());

        _dataTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _dataTimer.Tick += async (_, _) =>
        {
            if (UseLiveData)
                await RefreshRuntimeDataAsync();
            else if (Document.Widgets.Any(w => w.Type == WidgetType.AnalogClock))
            {
                ThemeChanged?.Invoke(this, EventArgs.Empty);
                if (LivePreview)
                    RequestLiveFrame?.Invoke(this, EventArgs.Empty);
            }
        };
        _dataTimer.Start();

        _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _animationTimer.Tick += (_, _) =>
        {
            if (!Document.Widgets.Any(w => w.IsVisible && w.Type == WidgetType.AnimatedImage))
                return;

            ThemeChanged?.Invoke(this, EventArgs.Empty);
        };
        _animationTimer.Start();

        AttachWorkspace(_workspace);
        CreateStarterLayout();
        RefreshPorts();
        RefreshThemes();
    }

    public event EventHandler? ThemeChanged;
    public event EventHandler? RequestLiveFrame;

    public ThemeWorkspace Workspace => _workspace;
    public ThemeDocument Document => _workspace.Document;
    public ObservableCollection<SerialPortOption> Ports { get; }
    public ObservableCollection<ThemeLibraryItem> Themes { get; }
    public ObservableCollection<ThemeAsset> FontAssets { get; }

    public ObservableCollection<string> DataSources { get; } =
    [
        "Preview.Value",
        "CPU.Usage", "CPU.Temperature", "CPU.Power", "CPU.Clock",
        "GPU.Usage", "GPU.Temperature", "GPU.Hotspot", "GPU.VRAM", "GPU.VRAMUsed", "GPU.VRAMTotal", "GPU.Power", "GPU.FanRPM",
        "RAM.Usage", "RAM.UsedGB", "RAM.AvailableGB", "RAM.TotalGB",
        "Disk.Usage", "Disk.FreeGB", "Disk.Temperature", "Disk.Read", "Disk.Write",
        "Network.Download", "Network.Upload",
        "Cooling.FanRPM", "Cooling.Fan1RPM", "Cooling.Fan2RPM", "Cooling.Fan3RPM",
        "Cooling.Fan4RPM", "Cooling.Fan5RPM", "Cooling.Fan6RPM", "Cooling.PumpRPM",
        "Weather.Temperature", "Weather.FeelsLike", "Weather.Humidity", "Weather.Wind",
        "Weather.WindDirection", "Weather.WindGust", "Weather.Condition", "Weather.Location",
        "Weather.Precipitation", "Weather.PrecipitationChance", "Weather.CloudCover", "Weather.Pressure",
        "Weather.DayNight", "Weather.TodayHigh", "Weather.TodayLow", "Weather.TodayCondition",
        "Weather.Sunrise", "Weather.Sunset",
        "Clock.Time", "Clock.Date", "Clock.Day"
    ];

    public IReadOnlyList<string> FontChoices { get; } =
    [
        "Segoe UI",
        "Arial",
        "Consolas",
        "Bungee",
        "Fredoka",
        "Monoton",
        "Orbitron"
    ];

    public Array GraphStyles => Enum.GetValues(typeof(GraphStyle));
    public Array MediaFits => Enum.GetValues(typeof(MediaFit));
    public Array ShapeStyles => Enum.GetValues(typeof(ShapeStyle));
    public IReadOnlyList<RotationOption> RotationOptions { get; } =
    [
        new("0°", DeviceRotation.Degrees0),
        new("90°", DeviceRotation.Degrees90),
        new("180°", DeviceRotation.Degrees180),
        new("270°", DeviceRotation.Degrees270)
    ];

    public IReadOnlyList<DisplayProtocolOption> DisplayProtocolOptions { get; } =
    [
        new("Auto (recommended)", DisplayProtocolProfile.Auto),
        new("Rev-A native portrait", DisplayProtocolProfile.RevANativePortrait),
        new("Rev-A hardware landscape", DisplayProtocolProfile.RevAHardwareLogical),
        new("Rev-A legacy 320×480", DisplayProtocolProfile.RevAHardwareNative)
    ];

    public IReadOnlyList<DisplayColorOption> DisplayColorOptions { get; } =
    [
        new("Auto (RGB565 LE)", DisplayColorMode.Auto),
        new("RGB565 little-endian", DisplayColorMode.Rgb565LittleEndian),
        new("BGR565 little-endian", DisplayColorMode.Bgr565LittleEndian),
        new("RGB565 byte-swapped", DisplayColorMode.Rgb565BigEndian),
        new("BGR565 byte-swapped", DisplayColorMode.Bgr565BigEndian)
    ];

    public WidgetModel? SelectedWidget
    {
        get => _selectedWidget;
        set
        {
            if (_selectedWidget == value) return;
            if (_selectedWidget is not null) _selectedWidget.IsSelected = false;
            _selectedWidget = value;
            if (_selectedWidget is not null) _selectedWidget.IsSelected = true;
            RaisePropertyChanged();
            RaiseCommandStates();
        }
    }

    public string? SelectedPort
    {
        get => _selectedPort;
        set => SetProperty(ref _selectedPort, value);
    }

    public ThemeLibraryItem? SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (!SetProperty(ref _selectedTheme, value)) return;
            LoadThemeCommand.RaiseCanExecuteChanged();
        }
    }

    public string DeviceStatus
    {
        get => _deviceStatus;
        private set => SetProperty(ref _deviceStatus, value);
    }

    public DisplayProtocolProfile DisplayProtocol
    {
        get => _displayProtocol;
        set
        {
            if (!SetProperty(ref _displayProtocol, value)) return;
            _appSettings.DisplayProtocol = value;
            _settingsService.Save(_appSettings);

            if (_deviceService.IsConnected && !IsDeviceBusy)
                _ = ApplyDisplayCompatibilityAsync();
        }
    }

    public DisplayColorMode DisplayColorMode
    {
        get => _displayColorMode;
        set
        {
            if (!SetProperty(ref _displayColorMode, value)) return;
            _appSettings.DisplayColorMode = value;
            _settingsService.Save(_appSettings);

            if (_deviceService.IsConnected && !IsDeviceBusy)
                _ = ApplyDisplayCompatibilityAsync();
        }
    }

    public string WeatherCity
    {
        get => _weatherCity;
        set => SetProperty(ref _weatherCity, value);
    }

    public string WeatherStatus
    {
        get => _weatherStatus;
        private set => SetProperty(ref _weatherStatus, value);
    }

    public string HardwareStatus
    {
        get => _hardwareStatus;
        private set => SetProperty(ref _hardwareStatus, value);
    }

    public bool IsHardwareElevated => _hardwareMetrics.IsElevated;
    public bool IsLowLevelSensorDriverInstalled => _hardwareMetrics.IsLowLevelDriverInstalled;
    public bool NeedsFullSensorAccess => !IsHardwareElevated || !IsLowLevelSensorDriverInstalled;

    public bool LivePreview
    {
        get => _livePreview;
        set
        {
            if (!SetProperty(ref _livePreview, value)) return;
            if (value) RequestLiveFrame?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool UseLiveData
    {
        get => _useLiveData;
        set
        {
            if (!SetProperty(ref _useLiveData, value)) return;
            if (value)
                _ = RefreshRuntimeDataAsync();
            else
                ClearRuntimeData();
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsDeviceBusy
    {
        get => _isDeviceBusy;
        private set
        {
            if (!SetProperty(ref _isDeviceBusy, value)) return;
            ConnectCommand.RaiseCanExecuteChanged();
            DetectScreenCommand.RaiseCanExecuteChanged();
            TestScreenCommand.RaiseCanExecuteChanged();
            BenchmarkCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsDirty => Workspace.IsDirty;
    public string WindowTitle => $"{Document.Name}{(IsDirty ? " *" : string.Empty)} - PC Info Screen Studio";

    public RelayCommand NewCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand SaveAsCommand { get; }
    public RelayCommand AddWidgetCommand { get; }
    public RelayCommand DeleteWidgetCommand { get; }
    public RelayCommand DuplicateWidgetCommand { get; }
    public RelayCommand MoveLayerUpCommand { get; }
    public RelayCommand MoveLayerDownCommand { get; }
    public RelayCommand ImportImageCommand { get; }
    public RelayCommand ImportGifCommand { get; }
    public RelayCommand ImportVideoCommand { get; }
    public RelayCommand ImportFontCommand { get; }
    public RelayCommand ToggleOrientationCommand { get; }
    public RelayCommand RotateDeviceCommand { get; }
    public RelayCommand RefreshPortsCommand { get; }
    public RelayCommand DetectScreenCommand { get; }
    public RelayCommand ConnectCommand { get; }
    public RelayCommand TestScreenCommand { get; }
    public RelayCommand BenchmarkCommand { get; }
    public RelayCommand RefreshThemesCommand { get; }
    public RelayCommand LoadThemeCommand { get; }
    public RelayCommand UpdateWeatherCommand { get; }
    public RelayCommand RestartElevatedCommand { get; }
    public RelayCommand EnableFullSensorsCommand { get; }

    public void SelectWidget(WidgetModel? widget) => SelectedWidget = widget;

    public void NotifyDesignerChange()
    {
        MarkDirty();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SendLiveFrame(SkiaSharp.SKBitmap bitmap, bool force = false)
    {
        if ((!LivePreview && !force) || !_deviceService.IsConnected) return;
        if (DateTimeOffset.UtcNow < _suspendLiveDisplayUntil) return;

        if (Interlocked.CompareExchange(ref _frameSendBusy, 1, 0) != 0)
            return;

        var copy = bitmap.Copy();
        var rotation = Document.DeviceRotation;
        _ = SendLiveFrameAsync(copy, rotation);
    }

    private async Task SendLiveFrameAsync(SkiaSharp.SKBitmap bitmap, DeviceRotation rotation)
    {
        try
        {
            await _deviceService.DisplayAsync(bitmap, rotation);
            await Application.Current.Dispatcher.InvokeAsync(() =>
                DeviceStatus = BuildConnectionStatus());
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
                DeviceStatus = "Display error: " + ex.Message);
        }
        finally
        {
            bitmap.Dispose();
            Interlocked.Exchange(ref _frameSendBusy, 0);
        }
    }

    private void NewTheme()
    {
        if (!ConfirmDiscardIfNeeded()) return;
        ReplaceWorkspace(_packageService.CreateNewWorkspace());
        CreateStarterLayout();
    }

    private async Task OpenThemeAsync()
    {
        var path = _dialogs.OpenTheme();
        if (path is null) return;
        await OpenThemeFileAsync(path);
    }

    public async Task OpenThemeFileAsync(string path)
    {
        if (!ConfirmDiscardIfNeeded()) return;
        try
        {
            var workspace = await _packageService.LoadAsync(path);
            ReplaceWorkspace(workspace);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not open theme", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task SaveAsync(bool saveAs)
    {
        var path = saveAs || string.IsNullOrWhiteSpace(Workspace.FilePath)
            ? _dialogs.SaveTheme(Document.Name)
            : Workspace.FilePath;
        if (path is null) return;

        try
        {
            await _packageService.SaveAsync(Workspace, path);
            RaisePropertyChanged(nameof(IsDirty));
            RaisePropertyChanged(nameof(WindowTitle));
            RefreshThemes();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not save theme", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddWidget(object? parameter)
    {
        if (parameter is null || !Enum.TryParse<WidgetType>(parameter.ToString(), out var type)) return;
        var widget = NewWidget(type);
        Document.Widgets.Insert(0, widget);
        NormalizeZIndices();
        SelectedWidget = widget;
        MarkDirtyAndRefresh();
    }

    private WidgetModel NewWidget(WidgetType type)
    {
        var centerX = Math.Max(8, Document.CanvasWidth / 2.0 - 70);
        var centerY = Math.Max(8, Document.CanvasHeight / 2.0 - 40);
        return type switch
        {
            WidgetType.Text => new WidgetModel { Type = type, Name = "Text", Label = "Text", Width = 140, Height = 45, X = centerX, Y = centerY, FontSize = 24, Suffix = "" },
            WidgetType.Value => new WidgetModel { Type = type, Name = "Value", Label = "CPU", DataSource = "CPU.Usage", Width = 130, Height = 70, X = centerX, Y = centerY, FontSize = 30 },
            WidgetType.CircularGauge => new WidgetModel { Type = type, Name = "Circular gauge", Label = "CPU Usage", DataSource = "CPU.Usage", Width = 110, Height = 110, X = centerX, Y = centerY, FontSize = 26 },
            WidgetType.AnalogClock => new WidgetModel { Type = type, Name = "Traditional clock", Label = "Clock", DataSource = "Clock.Time", Width = 120, Height = 120, X = centerX, Y = centerY, FontSize = 18, ShowLabel = false, ShowValue = false },
            WidgetType.BarGauge => new WidgetModel { Type = type, Name = "Bar gauge", Label = "RAM Usage", DataSource = "RAM.Usage", Width = 180, Height = 62, X = centerX, Y = centerY, FontSize = 22, SegmentCount = 18 },
            WidgetType.Graph => new WidgetModel { Type = type, Name = "Graph", Label = "GPU TEMP", DataSource = "GPU.Temperature", Width = 210, Height = 95, X = centerX, Y = centerY, FontSize = 22, Suffix = "°C", SimulatedValue = 52, Maximum = 100, GraphStyle = GraphStyle.Blocks },
            WidgetType.Shape => new WidgetModel { Type = type, Name = "Shape", Width = 160, Height = 80, X = centerX, Y = centerY, BackgroundColor = "#301A1E26", AccentColor = "#FF67717F" },
            _ => new WidgetModel { Type = type, Name = type.ToString(), Width = 200, Height = 120, X = centerX, Y = centerY }
        };
    }

    private void ImportMedia(ThemeAssetKind kind)
    {
        string? path = kind switch
        {
            ThemeAssetKind.Image => _dialogs.OpenImage(),
            ThemeAssetKind.Gif => _dialogs.OpenAnimatedImage(),
            ThemeAssetKind.Video => _dialogs.OpenVideo(),
            _ => null
        };
        if (path is null) return;
        ImportMediaFile(path, kind);
    }

    public void ImportMediaFile(string path, ThemeAssetKind kind)
    {
        try
        {
            var asset = _assetService.Import(Workspace, path, kind);
            var type = kind switch
            {
                ThemeAssetKind.Image => WidgetType.Image,
                ThemeAssetKind.Gif => WidgetType.AnimatedImage,
                _ => WidgetType.Video
            };
            var widget = NewWidget(type);
            widget.Name = asset.DisplayName;
            widget.AssetId = asset.Id;
            widget.X = 0;
            widget.Y = 0;
            widget.Width = Document.CanvasWidth;
            widget.Height = Document.CanvasHeight;
            Document.Widgets.Insert(0, widget);
            NormalizeZIndices();
            SelectedWidget = widget;
            MarkDirtyAndRefresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not import media", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportFont()
    {
        var path = _dialogs.OpenFont();
        if (path is null) return;
        ImportFontFile(path);
    }

    public void ImportFontFile(string path)
    {
        var result = MessageBox.Show(
            "The font will be embedded inside the shareable theme file. Only continue if its licence allows redistribution.\n\nEmbed this font?",
            "Embed font", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            var asset = _assetService.Import(Workspace, path, ThemeAssetKind.Font);
            FontAssets.Add(asset);
            if (SelectedWidget is not null)
                SelectedWidget.FontAssetId = asset.Id;
            MarkDirty();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not import font", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteSelected()
    {
        if (SelectedWidget is null) return;
        var widget = SelectedWidget;
        SelectedWidget = null;
        Document.Widgets.Remove(widget);
        MarkDirty();
    }

    private void DuplicateSelected()
    {
        if (SelectedWidget is null) return;
        var clone = SelectedWidget.Clone();
        var selectedIndex = Document.Widgets.IndexOf(SelectedWidget);
        Document.Widgets.Insert(Math.Max(0, selectedIndex), clone);
        NormalizeZIndices();
        SelectedWidget = clone;
        MarkDirtyAndRefresh();
    }

    private void MoveLayer(int delta)
    {
        if (SelectedWidget is null) return;

        var current = Document.Widgets.IndexOf(SelectedWidget);
        if (current < 0) return;

        var target = Math.Clamp(current - delta, 0, Document.Widgets.Count - 1);
        if (target == current) return;

        Document.Widgets.Move(current, target);
        NormalizeZIndices();
        MarkDirtyAndRefresh();
    }

    private void NormalizeZIndices()
    {
        for (var i = 0; i < Document.Widgets.Count; i++)
            Document.Widgets[i].ZIndex = Document.Widgets.Count - 1 - i;
    }

    private void ToggleOrientation()
    {
        var oldW = Document.CanvasWidth;
        var oldH = Document.CanvasHeight;
        Document.Orientation = Document.Orientation == ThemeOrientation.Landscape ? ThemeOrientation.Portrait : ThemeOrientation.Landscape;
        var scaleX = Document.CanvasWidth / (double)oldW;
        var scaleY = Document.CanvasHeight / (double)oldH;
        foreach (var w in Document.Widgets)
        {
            w.X *= scaleX; w.Y *= scaleY;
            w.Width = Math.Min(Document.CanvasWidth - w.X, w.Width * scaleX);
            w.Height = Math.Min(Document.CanvasHeight - w.Y, w.Height * scaleY);
        }
        MarkDirtyAndRefresh();
    }

    private void RotateDevice()
    {
        Document.DeviceRotation = Document.DeviceRotation switch
        {
            DeviceRotation.Degrees0 => DeviceRotation.Degrees90,
            DeviceRotation.Degrees90 => DeviceRotation.Degrees180,
            DeviceRotation.Degrees180 => DeviceRotation.Degrees270,
            _ => DeviceRotation.Degrees0
        };
        if (_deviceService.IsConnected)
            _ = ApplyDeviceOrientationAsync();

        MarkDirtyAndRefresh();
    }

    private void RefreshPorts()
    {
        var previous = SelectedPort;
        Ports.Clear();

        foreach (var port in _serialDiscovery.Discover())
            Ports.Add(port);

        if (!string.IsNullOrWhiteSpace(previous) &&
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

    private void DetectScreen()
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

    private async Task ConnectOrDisconnectAsync()
    {
        if (IsDeviceBusy) return;

        if (_deviceService.IsConnected)
        {
            IsDeviceBusy = true;
            DeviceStatus = "Disconnecting...";
            try
            {
                await _deviceService.DisconnectAsync();
                DeviceStatus = "Not connected";
            }
            catch (Exception ex)
            {
                DeviceStatus = "Disconnect error: " + ex.Message;
            }
            finally
            {
                IsDeviceBusy = false;
                BenchmarkCommand.RaiseCanExecuteChanged();
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedPort))
        {
            MessageBox.Show("Choose a COM port first.", "Connect display", MessageBoxButton.OK, MessageBoxImage.Information);
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

            DeviceStatus = BuildConnectionStatus();
            RequestLiveFrame?.Invoke(this, EventArgs.Empty);
        }
        catch (UnauthorizedAccessException)
        {
            DeviceStatus = "Connection failed: port is in use";
            MessageBox.Show(
                $"{SelectedPort} is already in use by another program.\n\nClose the original screen software (including its tray icon), a serial monitor, or any other program using this COM port, then try again.",
                "Could not connect display",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            DeviceStatus = "Connection failed";
            MessageBox.Show(ex.Message, "Could not connect display", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsDeviceBusy = false;
            BenchmarkCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task TestScreenAsync()
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

            if (LivePreview && _deviceService.IsConnected)
                await Application.Current.Dispatcher.InvokeAsync(() => RequestLiveFrame?.Invoke(this, EventArgs.Empty));
        }
        catch
        {
            // Diagnostic display restoration is best-effort.
        }
    }

    private async Task ApplyDisplayCompatibilityAsync()
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
                RequestLiveFrame?.Invoke(this, EventArgs.Empty);
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

    private async Task ApplyDeviceOrientationAsync()
    {
        try
        {
            await _deviceService.ApplyOrientationAsync(Document.Orientation, Document.DeviceRotation);
            if (LivePreview)
                RequestLiveFrame?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            DeviceStatus = "Rotation error: " + ex.Message;
        }
    }

    private string BuildConnectionStatus()
        => $"Connected: {_deviceService.ConnectedModel ?? "screen"} on {_deviceService.ConnectedPort} " +
           $"@ {_deviceService.ConnectedBaudRate ?? 0} baud · {_deviceService.ConnectedProtocol} · {_deviceService.ConnectedColorMode}";

    private async Task RunBenchmarkAsync()
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

    private void RefreshThemes()
    {
        var previousPath = SelectedTheme?.FilePath;
        var previousBuiltIn = SelectedTheme?.BuiltInId;

        Themes.Clear();
        foreach (var theme in _themeLibrary.GetThemes(Workspace.FilePath))
            Themes.Add(theme);

        SelectedTheme = Themes.FirstOrDefault(t =>
                            !string.IsNullOrWhiteSpace(previousPath) &&
                            string.Equals(t.FilePath, previousPath, StringComparison.OrdinalIgnoreCase))
                        ?? Themes.FirstOrDefault(t =>
                            !string.IsNullOrWhiteSpace(previousBuiltIn) &&
                            string.Equals(t.BuiltInId, previousBuiltIn, StringComparison.OrdinalIgnoreCase))
                        ?? Themes.FirstOrDefault();
    }

    private async Task LoadSelectedThemeAsync()
    {
        var selection = SelectedTheme;
        if (selection is null) return;

        if (selection.IsBuiltIn)
        {
            if (!ConfirmDiscardIfNeeded()) return;

            ReplaceWorkspace(_packageService.CreateNewWorkspace());
            if (selection.BuiltInId == "blank")
                CreateBlankLayout();
            else
                CreateStarterLayout();

            return;
        }

        if (!string.IsNullOrWhiteSpace(selection.FilePath))
            await OpenThemeFileAsync(selection.FilePath);
    }

    private void CreateBlankLayout()
    {
        _suppressDirty = true;
        try
        {
            Document.Name = "Blank theme";
            Document.Widgets.Clear();
            Workspace.IsDirty = false;
            SelectedWidget = null;
        }
        finally
        {
            _suppressDirty = false;
        }

        ThemeChanged?.Invoke(this, EventArgs.Empty);
        RaisePropertyChanged(nameof(WindowTitle));
    }

    private void CreateStarterLayout()
    {
        _suppressDirty = true;
        try
        {
            Document.Name = "New theme";
            Document.Widgets.Clear();
            var cpu = NewWidget(WidgetType.CircularGauge);
            cpu.X = 25; cpu.Y = 52; cpu.Width = 108; cpu.Height = 108; cpu.Label = "CPU"; cpu.SimulatedValue = 42;
            var gpu = NewWidget(WidgetType.Graph);
            gpu.X = 160; gpu.Y = 54; gpu.Width = 285; gpu.Height = 104; gpu.Label = "GPU TEMP"; gpu.GraphStyle = GraphStyle.Blocks;
            var ram = NewWidget(WidgetType.BarGauge);
            ram.X = 25; ram.Y = 210; ram.Width = 420; ram.Height = 62; ram.Label = "RAM"; ram.SimulatedValue = 64;
            Document.Widgets.Add(cpu); Document.Widgets.Add(gpu); Document.Widgets.Add(ram);
            NormalizeZIndices();
            Workspace.IsDirty = false;
            SelectedWidget = cpu;
        }
        finally { _suppressDirty = false; }
        ThemeChanged?.Invoke(this, EventArgs.Empty);
        RaisePropertyChanged(nameof(WindowTitle));
    }

    private void ReplaceWorkspace(ThemeWorkspace workspace)
    {
        DetachWorkspace(_workspace);
        _workspace.Dispose();
        _workspace = workspace;
        AttachWorkspace(_workspace);
        FontAssets.Clear();
        foreach (var font in Document.Assets.Where(a => a.Kind == ThemeAssetKind.Font)) FontAssets.Add(font);
        SelectedWidget = Document.Widgets.OrderBy(w => w.ZIndex).FirstOrDefault();
        RaisePropertyChanged(nameof(Workspace));
        RaisePropertyChanged(nameof(Document));
        RaisePropertyChanged(nameof(IsDirty));
        RaisePropertyChanged(nameof(WindowTitle));
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AttachWorkspace(ThemeWorkspace workspace)
    {
        workspace.Document.PropertyChanged += OnDocumentPropertyChanged;
        workspace.Document.Widgets.CollectionChanged += OnWidgetsChanged;
        workspace.Document.Assets.CollectionChanged += OnAssetsChanged;
        foreach (var w in workspace.Document.Widgets) w.PropertyChanged += OnWidgetPropertyChanged;
    }

    private void DetachWorkspace(ThemeWorkspace workspace)
    {
        workspace.Document.PropertyChanged -= OnDocumentPropertyChanged;
        workspace.Document.Widgets.CollectionChanged -= OnWidgetsChanged;
        workspace.Document.Assets.CollectionChanged -= OnAssetsChanged;
        foreach (var w in workspace.Document.Widgets) w.PropertyChanged -= OnWidgetPropertyChanged;
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        MarkDirtyAndRefresh();

        if (_deviceService.IsConnected &&
            e.PropertyName is nameof(ThemeDocument.Orientation) or nameof(ThemeDocument.DeviceRotation))
        {
            _ = ApplyDeviceOrientationAsync();
        }
    }
    private void OnWidgetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WidgetModel.IsSelected) or nameof(WidgetModel.RuntimeValue) or nameof(WidgetModel.RuntimeText) or nameof(WidgetModel.RuntimeUnit)) return;
        if (e.PropertyName == nameof(WidgetModel.DataSource) && sender is WidgetModel widget)
            ApplyDataSourceDefaults(widget);
        MarkDirtyAndRefresh();
    }

    private static void ApplyDataSourceDefaults(WidgetModel widget)
    {
        var source = widget.DataSource ?? string.Empty;
        widget.Label = FriendlyLabel(source);
        if (source.StartsWith("Clock.", StringComparison.OrdinalIgnoreCase) ||
            source is "Weather.Condition" or "Weather.Location" or "Weather.DayNight" or
                "Weather.TodayCondition" or "Weather.Sunrise" or "Weather.Sunset")
        {
            widget.Suffix = string.Empty;
            return;
        }

        if (source.Equals("Weather.WindDirection", StringComparison.OrdinalIgnoreCase))
        {
            widget.Suffix = "°"; widget.Minimum = 0; widget.Maximum = 360; return;
        }

        if (source.Equals("Weather.Wind", StringComparison.OrdinalIgnoreCase) ||
            source.Equals("Weather.WindGust", StringComparison.OrdinalIgnoreCase))
        {
            widget.Suffix = " km/h"; widget.Minimum = 0; widget.Maximum = Math.Max(150, widget.Maximum); return;
        }

        if (source.Equals("Weather.Pressure", StringComparison.OrdinalIgnoreCase))
        {
            widget.Suffix = " hPa"; widget.Minimum = 900; widget.Maximum = 1100; return;
        }

        if (source.Equals("Weather.Precipitation", StringComparison.OrdinalIgnoreCase))
        {
            widget.Suffix = " mm"; widget.Minimum = 0; widget.Maximum = Math.Max(50, widget.Maximum); return;
        }

        if (source.Equals("Weather.Humidity", StringComparison.OrdinalIgnoreCase) ||
            source.Equals("Weather.CloudCover", StringComparison.OrdinalIgnoreCase) ||
            source.Equals("Weather.PrecipitationChance", StringComparison.OrdinalIgnoreCase))
        {
            widget.Suffix = "%"; widget.Minimum = 0; widget.Maximum = 100; return;
        }

        if (IsTemperatureSource(source))
        {
            widget.Suffix = RegionalFormatService.TemperatureSuffix;
            widget.Minimum = RegionalFormatService.UsesFahrenheit ? 20 : -10;
            widget.Maximum = RegionalFormatService.UsesFahrenheit ? 230 : 110;
            return;
        }
        if (source.EndsWith("Usage", StringComparison.OrdinalIgnoreCase) || source.EndsWith("VRAM", StringComparison.OrdinalIgnoreCase))
        {
            widget.Suffix = "%"; widget.Minimum = 0; widget.Maximum = 100; return;
        }
        if (source is "GPU.VRAMUsed" or "GPU.VRAMTotal")
        {
            widget.Suffix = " MB"; widget.Minimum = 0; widget.Maximum = Math.Max(16384, widget.Maximum); return;
        }
        if (source.EndsWith("GB", StringComparison.OrdinalIgnoreCase))
        {
            widget.Suffix = " GB"; widget.Minimum = 0; widget.Maximum = Math.Max(64, widget.Maximum); return;
        }
        if (source.Contains("Network.", StringComparison.OrdinalIgnoreCase) || source.EndsWith("Read", StringComparison.OrdinalIgnoreCase) || source.EndsWith("Write", StringComparison.OrdinalIgnoreCase))
        {
            widget.Suffix = " MB/s"; widget.Minimum = 0; widget.Maximum = Math.Max(100, widget.Maximum); return;
        }
        if (source.Contains("FanRPM", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("PumpRPM", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("[Fan]", StringComparison.OrdinalIgnoreCase))
        {
            widget.Suffix = " RPM"; widget.Minimum = 0; widget.Maximum = Math.Max(5000, widget.Maximum); return;
        }
        if (source.EndsWith("Power", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("[Power]", StringComparison.OrdinalIgnoreCase))
        {
            widget.Suffix = " W"; widget.Minimum = 0; widget.Maximum = Math.Max(400, widget.Maximum); return;
        }
        if (source.EndsWith("Clock", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("[Clock]", StringComparison.OrdinalIgnoreCase))
        {
            widget.Suffix = " MHz"; widget.Minimum = 0; widget.Maximum = Math.Max(6000, widget.Maximum); return;
        }
        if (source.Contains("[Load]", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("[Usage]", StringComparison.OrdinalIgnoreCase))
        {
            widget.Suffix = "%"; widget.Minimum = 0; widget.Maximum = 100;
        }
    }

    private static string FriendlyLabel(string source)
    {
        if (source.StartsWith("Sensor: ", StringComparison.OrdinalIgnoreCase))
        {
            var body = source["Sensor: ".Length..];
            var parts = body.Split(" / ", StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                var sensor = parts[^1];
                var bracket = sensor.LastIndexOf(" [", StringComparison.Ordinal);
                return bracket > 0 ? sensor[..bracket] : sensor;
            }

            return body;
        }

        return source switch
        {
            "Preview.Value" => "Value",
            "CPU.Usage" => "CPU Usage",
            "CPU.Temperature" => "CPU Temp",
            "CPU.Power" => "CPU Power",
            "CPU.Clock" => "CPU Clock",
            "GPU.Usage" => "GPU Usage",
            "GPU.Temperature" => "GPU Temp",
            "GPU.Hotspot" => "GPU Hotspot",
            "GPU.VRAM" => "VRAM",
            "GPU.VRAMUsed" => "VRAM Used",
            "GPU.VRAMTotal" => "VRAM Total",
            "GPU.Power" => "GPU Power",
            "GPU.FanRPM" => "GPU Fan",
            "RAM.Usage" => "RAM Usage",
            "RAM.UsedGB" => "RAM Used",
            "RAM.AvailableGB" => "RAM Available",
            "RAM.TotalGB" => "RAM Total",
            "Disk.Usage" => "Disk Usage",
            "Disk.FreeGB" => "Disk Free",
            "Disk.Temperature" => "Disk Temp",
            "Disk.Read" => "Disk Read",
            "Disk.Write" => "Disk Write",
            "Network.Download" => "Download",
            "Network.Upload" => "Upload",
            "Cooling.FanRPM" => "Fan",
            "Cooling.Fan1RPM" => "Fan 1",
            "Cooling.Fan2RPM" => "Fan 2",
            "Cooling.Fan3RPM" => "Fan 3",
            "Cooling.Fan4RPM" => "Fan 4",
            "Cooling.Fan5RPM" => "Fan 5",
            "Cooling.Fan6RPM" => "Fan 6",
            "Cooling.PumpRPM" => "Pump",
            "Weather.Temperature" => "Temperature",
            "Weather.FeelsLike" => "Feels Like",
            "Weather.Humidity" => "Humidity",
            "Weather.Wind" => "Wind",
            "Weather.WindDirection" => "Wind Direction",
            "Weather.WindGust" => "Wind Gust",
            "Weather.Condition" => "Condition",
            "Weather.Location" => "Location",
            "Weather.Precipitation" => "Precipitation",
            "Weather.PrecipitationChance" => "Rain Chance",
            "Weather.CloudCover" => "Cloud Cover",
            "Weather.Pressure" => "Pressure",
            "Weather.DayNight" => "Day / Night",
            "Weather.TodayHigh" => "Today's High",
            "Weather.TodayLow" => "Today's Low",
            "Weather.TodayCondition" => "Today's Weather",
            "Weather.Sunrise" => "Sunrise",
            "Weather.Sunset" => "Sunset",
            "Clock.Time" => "Time",
            "Clock.Date" => "Date",
            "Clock.Day" => "Day",
            _ => source.Replace('.', ' ')
        };
    }

    private static bool IsTemperatureSource(string source)
        => source.Contains("Temperature", StringComparison.OrdinalIgnoreCase) ||
           source.Contains("Hotspot", StringComparison.OrdinalIgnoreCase) ||
           (source.Contains("[Temperature]", StringComparison.OrdinalIgnoreCase) || source.Contains("[Temp]", StringComparison.OrdinalIgnoreCase)) ||
           source.Equals("Weather.FeelsLike", StringComparison.OrdinalIgnoreCase) ||
           source.Equals("Weather.TodayHigh", StringComparison.OrdinalIgnoreCase) ||
           source.Equals("Weather.TodayLow", StringComparison.OrdinalIgnoreCase);

    private static MetricValue ApplyRegionalFormat(string source, MetricValue value)
    {
        if (value.Numeric is not double numeric || !IsTemperatureSource(source))
            return value;

        return new MetricValue(
            RegionalFormatService.ConvertTemperatureFromCelsius(numeric),
            value.Text,
            RegionalFormatService.TemperatureSuffix);
    }

    private void OnWidgetsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (WidgetModel w in e.OldItems) w.PropertyChanged -= OnWidgetPropertyChanged;
        if (e.NewItems is not null)
            foreach (WidgetModel w in e.NewItems) w.PropertyChanged += OnWidgetPropertyChanged;
        MarkDirtyAndRefresh();
    }

    private void OnAssetsChanged(object? sender, NotifyCollectionChangedEventArgs e) => MarkDirtyAndRefresh();

    private async Task RefreshRuntimeDataAsync(bool forceWeather = false)
    {
        if (!UseLiveData) return;

        if (Interlocked.CompareExchange(ref _dataSampleBusy, 1, 0) != 0)
            return;

        try
        {
            var systemTask = Task.Run(_systemMetrics.Sample);
            var hardwareTask = Task.Run(_hardwareMetrics.Sample);
            var weatherTask = _weatherMetrics.GetMetricsAsync(WeatherCity, forceWeather);

            await Task.WhenAll(systemTask, hardwareTask, weatherTask);

            var sample = new Dictionary<string, MetricValue>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in systemTask.Result)
                sample[pair.Key] = pair.Value;

            foreach (var pair in hardwareTask.Result)
            {
                sample[pair.Key] = pair.Value;

                if (pair.Key.StartsWith("Sensor: ", StringComparison.OrdinalIgnoreCase) &&
                    !DataSources.Contains(pair.Key))
                {
                    DataSources.Add(pair.Key);
                }
            }

            foreach (var pair in weatherTask.Result)
                sample[pair.Key] = pair.Value;

            HardwareStatus = _hardwareMetrics.Status;
            RaisePropertyChanged(nameof(IsHardwareElevated));
            RaisePropertyChanged(nameof(IsLowLevelSensorDriverInstalled));
            RaisePropertyChanged(nameof(NeedsFullSensorAccess));
            WeatherStatus = _weatherMetrics.Status;

            foreach (var widget in Document.Widgets)
            {
                if (sample.TryGetValue(widget.DataSource, out var rawValue))
                {
                    var value = ApplyRegionalFormat(widget.DataSource, rawValue);
                    widget.RuntimeValue = value.Numeric;
                    widget.RuntimeText = value.Text;
                    widget.RuntimeUnit = value.Unit;

                    if (value.Numeric is double numeric && IsTemperatureSource(widget.DataSource))
                        widget.RuntimeText = numeric.ToString(widget.ValueFormat, RegionalFormatService.Culture) + (value.Unit ?? string.Empty);
                    else if (widget.Type == WidgetType.Text && value.Numeric is double textNumeric)
                        widget.RuntimeText = textNumeric.ToString(widget.ValueFormat, RegionalFormatService.Culture) + (value.Unit ?? string.Empty);

                    if (widget.Type == WidgetType.Graph && value.Numeric is double graphValue)
                    {
                        widget.RuntimeSeries.Add(graphValue);
                        var maxSamples = Math.Clamp(widget.HistorySeconds, 5, 3600);
                        if (widget.RuntimeSeries.Count > maxSamples)
                            widget.RuntimeSeries.RemoveRange(0, widget.RuntimeSeries.Count - maxSamples);
                    }
                }
                else
                {
                    if (!widget.DataSource.Equals("Preview.Value", StringComparison.OrdinalIgnoreCase))
                    {
                        widget.RuntimeValue = null;
                        widget.RuntimeText = "N/A";
                        widget.RuntimeUnit = null;
                    }
                    else
                    {
                        widget.RuntimeValue = null;
                        widget.RuntimeText = null;
                        widget.RuntimeUnit = null;
                    }
                }
            }

            ThemeChanged?.Invoke(this, EventArgs.Empty);
            if (LivePreview)
                RequestLiveFrame?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            Interlocked.Exchange(ref _dataSampleBusy, 0);
        }
    }

    private async Task UpdateWeatherAsync()
    {
        WeatherCity = WeatherCity.Trim();
        _appSettings.WeatherCity = WeatherCity;
        _settingsService.Save(_appSettings);

        if (string.IsNullOrWhiteSpace(WeatherCity))
        {
            WeatherStatus = "Weather city not configured.";
            if (UseLiveData)
                await RefreshRuntimeDataAsync(forceWeather: true);
            return;
        }

        WeatherStatus = $"Looking up {WeatherCity}...";

        if (UseLiveData)
        {
            await RefreshRuntimeDataAsync(forceWeather: true);
        }
        else
        {
            await _weatherMetrics.GetMetricsAsync(WeatherCity, forceRefresh: true);
            WeatherStatus = _weatherMetrics.Status;
        }
    }

    private async Task EnableFullSensorsAsync()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var installedNow = false;

        if (!_hardwareMetrics.IsLowLevelDriverInstalled)
        {
            var answer = MessageBox.Show(
                "CPU Package temperature, CPU Package power and many motherboard sensors need the signed PawnIO hardware-access driver used by LibreHardwareMonitor.\n\nRunning PC Info Screen Studio as Administrator by itself does NOT install this driver.\n\nInstall PawnIO now using Windows Package Manager?",
                "Hardware sensors",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (answer != MessageBoxResult.Yes)
                return;

            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "winget",
                    Arguments = "install --exact --id namazso.PawnIO --accept-package-agreements --accept-source-agreements",
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = AppContext.BaseDirectory
                });

                if (process is null)
                    throw new InvalidOperationException("Windows Package Manager could not be started.");

                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    MessageBox.Show(
                        $"PawnIO setup returned exit code {process.ExitCode}.\n\nYou can install it manually from Windows Terminal with:\nwinget install --exact --id namazso.PawnIO",
                        "Hardware sensors",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                installedNow = true;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message + "\n\nYou can install PawnIO manually from Windows Terminal with:\nwinget install --exact --id namazso.PawnIO",
                    "Could not install sensor driver",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
        }

        // PawnIO installation is detected when LibreHardwareMonitor starts, so
        // a process restart is required even if this process is already elevated.
        if (installedNow || !IsHardwareElevated)
        {
            RestartElevated(forceRestart: installedNow);
            return;
        }

        MessageBox.Show(
            "Full sensor access is already enabled. Turn on 'Live data' to populate CPU, GPU, storage and cooling values.\n\nThe Sensors status line at the bottom reports the active provider and any remaining limitation.",
            "Hardware sensors",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void RestartElevated(bool forceRestart = false)
    {
        if (!OperatingSystem.IsWindows())
            return;

        if (IsHardwareElevated && !forceRestart)
        {
            MessageBox.Show(
                "PC Info Screen Studio is already running as administrator.",
                "Hardware sensors",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (Workspace.IsDirty)
        {
            var result = MessageBox.Show(
                "This theme has unsaved changes. Restarting will discard them. Continue?",
                "Restart PC Info Screen Studio",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            });

            Application.Current.Shutdown();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // User cancelled the UAC prompt.
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Could not restart PC Info Screen Studio",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ClearRuntimeData()
    {
        foreach (var widget in Document.Widgets)
        {
            widget.RuntimeValue = null;
            widget.RuntimeText = null;
            widget.RuntimeUnit = null;
            widget.RuntimeSeries.Clear();
        }
    }

    private void MarkDirtyAndRefresh()
    {
        if (_suppressDirty) return;
        MarkDirty();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
        if (LivePreview) RequestLiveFrame?.Invoke(this, EventArgs.Empty);
    }

    private void MarkDirty()
    {
        if (_suppressDirty) return;
        Workspace.IsDirty = true;
        RaisePropertyChanged(nameof(IsDirty));
        RaisePropertyChanged(nameof(WindowTitle));
    }

    private void RaiseCommandStates()
    {
        DeleteWidgetCommand.RaiseCanExecuteChanged();
        DuplicateWidgetCommand.RaiseCanExecuteChanged();
        MoveLayerUpCommand.RaiseCanExecuteChanged();
        MoveLayerDownCommand.RaiseCanExecuteChanged();
    }

    private bool ConfirmDiscardIfNeeded()
    {
        if (!Workspace.IsDirty) return true;
        var result = MessageBox.Show("This theme has unsaved changes. Continue and discard them?", "Unsaved changes", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }

    public sealed record RotationOption(string Label, DeviceRotation Value);

    public void Dispose()
    {
        _dataTimer.Stop();
        _animationTimer.Stop();
        _weatherMetrics.Dispose();
        _hardwareMetrics.Dispose();
        _deviceService.Dispose();
        DetachWorkspace(_workspace);
        _workspace.Dispose();
    }
}
