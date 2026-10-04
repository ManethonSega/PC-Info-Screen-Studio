using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;
using PCInfoScreenStudio.Rendering;
using PCInfoScreenStudio.Controls;

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
    private readonly PhotoAlbumPresetService _photoAlbumPresetService = new();
    private readonly ModeThemeService _modeThemeService = new();
    private readonly AppSettings _appSettings;
    private readonly DispatcherTimer _dataTimer;
    private readonly DispatcherTimer _animationTimer;
    private readonly DispatcherTimer _historyTimer;
    private readonly DispatcherTimer _recoveryTimer;
    private readonly DocumentHistoryService _historyService = new();
    private readonly List<string> _history = [];
    private readonly string _recoveryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCInfoScreenStudio",
        "Recovery",
        "recovery.t3theme");
    private ThemeWorkspace _workspace;
    private WidgetModel? _selectedWidget;
    private double? _alignmentGuideX;
    private double? _alignmentGuideY;
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
    private bool _suppressHistory;
    private bool _recoveryBusy;
    private int _historyIndex = -1;
    private readonly object _frameQueueSync = new();
    private PendingDisplayFrame? _pendingFrame;
    private bool _frameSenderRunning;
    private FileSystemWatcher? _photoFolderWatcher;
    private PhotoFrameItem? _selectedPhoto;
    private ModeThemeLibraryItem? _selectedModeTheme;
    private bool _isPhotoPlaying;
    private DateTimeOffset _photoStartedAt = DateTimeOffset.UtcNow;
    private DateTimeOffset _photoTransitionStartedAt = DateTimeOffset.UtcNow;
    private readonly Random _photoRandom = new();
    private readonly HashSet<string> _advancedSensorSources = new(StringComparer.OrdinalIgnoreCase);
    private int _dataSampleBusy;
    private DateTimeOffset _suspendLiveDisplayUntil = DateTimeOffset.MinValue;
    private bool _isEditorActive = true;
    private bool _isFirstRunVisible;
    private bool _firstRunUseStarterTheme = true;
    private string _firstRunStatus = "Connect your screen now, or finish setup and connect later.";
    private string _addSearchText = string.Empty;

    public event EventHandler? AlignmentGuidesChanged;

    public MainViewModel()
    {
        _appSettings = _settingsService.Load();
        _weatherCity = _appSettings.WeatherCity;
        _displayProtocol = _appSettings.DisplayProtocol;
        _displayColorMode = _appSettings.DisplayColorMode;
        _isPhotoPlaying = _appSettings.PhotoFramePlaying;
        _workspace = _packageService.CreateNewWorkspace();
        Ports = [];
        Themes = [];
        ModeThemes = [];
        FontAssets = [];

        NewCommand = new RelayCommand(NewTheme);
        OpenCommand = new RelayCommand(async () => await OpenThemeAsync());
        SaveCommand = new RelayCommand(async () => await SaveAsync(false));
        SaveAsCommand = new RelayCommand(async () => await SaveAsync(true));
        UndoCommand = new RelayCommand(Undo, () => _historyIndex > 0);
        RedoCommand = new RelayCommand(Redo, () => _historyIndex >= 0 && _historyIndex < _history.Count - 1);
        NudgeWidgetCommand = new RelayCommand(NudgeSelected, _ => SelectedWidgets.Any(w => !w.IsLocked));
        AlignWidgetCommand = new RelayCommand(AlignSelected, _ => SelectedWidgets.Any(w => !w.IsLocked));
        GroupSelectedCommand = new RelayCommand(GroupSelected, () => SelectedWidgets.Count >= 2);
        UngroupSelectedCommand = new RelayCommand(UngroupSelected, () => SelectedWidgets.Any(w => w.GroupId is not null));
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
        DuplicateThemeCommand = new RelayCommand(DuplicateSelectedTheme, () => SelectedTheme is { IsBuiltIn: false });
        RenameThemeCommand = new RelayCommand(RenameSelectedTheme, () => SelectedTheme is { IsBuiltIn: false });
        DeleteThemeCommand = new RelayCommand(DeleteSelectedTheme, () => SelectedTheme is { IsBuiltIn: false });
        UpdateWeatherCommand = new RelayCommand(() => _ = UpdateWeatherAsync());
        RestartElevatedCommand = new RelayCommand(() => RestartElevated());
        EnableFullSensorsCommand = new RelayCommand(() => _ = EnableFullSensorsAsync());
        AddPhotosCommand = new RelayCommand(AddPhotos);
        AddPhotoFolderCommand = new RelayCommand(AddPhotoFolder);
        RemovePhotoCommand = new RelayCommand(RemoveSelectedPhoto, () => SelectedPhoto is not null);
        PreviousPhotoCommand = new RelayCommand(PreviousPhoto, () => Document.PhotoFrame.Photos.Count > 0);
        TogglePhotoPlaybackCommand = new RelayCommand(TogglePhotoPlayback, () => Document.PhotoFrame.Photos.Count > 0);
        NextPhotoCommand = new RelayCommand(NextPhoto, () => Document.PhotoFrame.Photos.Count > 0);
        SaveAlbumPresetCommand = new RelayCommand(SaveAlbumPreset, () => Document.PhotoFrame.Photos.Count > 0);
        LoadAlbumPresetCommand = new RelayCommand(LoadAlbumPreset);
        ChooseWatchedFolderCommand = new RelayCommand(ChooseWatchedFolder);
        SaveModeThemeCommand = new RelayCommand(SaveModeTheme, () => Document.Mode != ScreenMode.InfoScreen);
        LoadModeThemeCommand = new RelayCommand(LoadModeTheme, () => SelectedModeTheme is not null);
        DeleteModeThemeCommand = new RelayCommand(DeleteModeTheme, () => SelectedModeTheme is not null);
        AddCatalogItemCommand = new RelayCommand(AddCatalogItem);
        FirstRunConnectCommand = new RelayCommand(() => _ = DetectAndConnectFirstRunAsync(), () => !IsDeviceBusy);
        CompleteFirstRunCommand = new RelayCommand(CompleteFirstRun);

        _dataTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _dataTimer.Tick += async (_, _) =>
        {
            if (!IsEditorActive && !LivePreview)
                return;
            if (UseLiveData)
                await RefreshRuntimeDataAsync();
            else if (RuntimeWidgets.Any(w => w.Type == WidgetType.AnalogClock))
            {
                if (IsEditorActive)
                    ThemeChanged?.Invoke(this, EventArgs.Empty);
                if (LivePreview)
                    RequestLiveFrame?.Invoke(this, EventArgs.Empty);
            }
        };
        _dataTimer.Start();

        _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _animationTimer.Tick += (_, _) =>
        {
            if (!IsEditorActive && !LivePreview)
                return;
            var animations = RuntimeWidgets
                .Where(w => w.IsVisible && w.Type == WidgetType.AnimatedImage)
                .ToArray();
            var photoActive = Document.RuntimeMode is RuntimeScreenMode.PhotoFrame or RuntimeScreenMode.Hybrid
                && Document.PhotoFrame.Photos.Count > 0;
            var photoNeedsRender = photoActive && UpdatePhotoPlayback();

            if (animations.Length == 0 && !photoNeedsRender)
                return;

            var requestedFps = animations.Length == 0
                ? 10
                : animations.Max(w => w.TargetFps > 0
                    ? Math.Clamp(w.TargetFps, 1, 60)
                    : 30);
            if (photoActive && Document.PhotoFrame.RuntimeTransitionProgress < 1)
                requestedFps = Math.Max(requestedFps, 20);
            _animationTimer.Interval = TimeSpan.FromMilliseconds(1000d / requestedFps);
            if (IsEditorActive)
                ThemeChanged?.Invoke(this, EventArgs.Empty);
            if (LivePreview)
                RequestLiveFrame?.Invoke(this, EventArgs.Empty);
        };
        _animationTimer.Start();

        _historyTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _historyTimer.Tick += (_, _) =>
        {
            _historyTimer.Stop();
            CaptureHistoryNow();
        };

        _recoveryTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(8)
        };
        _recoveryTimer.Tick += async (_, _) => await SaveRecoveryAsync();
        _recoveryTimer.Start();

        AttachWorkspace(_workspace);
        CreateStarterLayout();
        InitializePhotoFrameRuntime();
        InitializeHistory();
        RefreshPorts();
        RefreshThemes();
        RefreshModeThemes();
        _isFirstRunVisible = !_appSettings.FirstRunCompleted;
    }

    public event EventHandler? ThemeChanged;
    public event EventHandler? RequestLiveFrame;
    public event EventHandler? EditorActivityChanged;

    public ThemeWorkspace Workspace => _workspace;
    public ThemeDocument Document => _workspace.Document;
    private IEnumerable<WidgetModel> RuntimeWidgets => Document.RuntimeMode == RuntimeScreenMode.Hybrid ? Document.HybridWidgets : Document.Widgets;
    private IEnumerable<WidgetModel> AllWidgets => Document.Widgets.Concat(Document.HybridWidgets);
    public ObservableCollection<SerialPortOption> Ports { get; }
    public ObservableCollection<ThemeLibraryItem> Themes { get; }
    public ObservableCollection<ModeThemeLibraryItem> ModeThemes { get; }
    public ObservableCollection<ThemeAsset> FontAssets { get; }

    public IReadOnlyList<AddWidgetOption> AddCatalog { get; } =
    [
        new("CPU usage", "Circular gauge · ready to use", WidgetType.CircularGauge, "CPU.Usage", "processor load percent circle"),
        new("CPU temperature", "Live temperature value", WidgetType.Value, "CPU.Temperature", "processor temp heat"),
        new("GPU usage", "Circular gauge · ready to use", WidgetType.CircularGauge, "GPU.Usage", "graphics load percent circle"),
        new("GPU temperature", "Live history graph", WidgetType.Graph, "GPU.Temperature", "graphics temp heat chart"),
        new("RAM usage", "Segmented horizontal gauge", WidgetType.BarGauge, "RAM.Usage", "memory percent bar"),
        new("Clock", "Traditional analogue clock", WidgetType.AnalogClock, "Clock.Time", "time watch"),
        new("Date", "Current date text", WidgetType.Value, "Clock.Date", "day calendar"),
        new("Text", "Custom text label", WidgetType.Text, null, "label heading"),
        new("Value", "Label and live value", WidgetType.Value, "CPU.Usage", "sensor number"),
        new("Circle", "Circular sensor gauge", WidgetType.CircularGauge, "CPU.Usage", "dial ring"),
        new("Bar", "Segmented sensor gauge", WidgetType.BarGauge, "RAM.Usage", "horizontal meter"),
        new("Graph", "Live history graph", WidgetType.Graph, "GPU.Temperature", "chart history"),
        new("Shape", "Rectangle or ellipse", WidgetType.Shape, null, "background panel")
    ];

    public IEnumerable<AddWidgetOption> FilteredAddCatalog
    {
        get
        {
            var query = AddSearchText.Trim();
            return string.IsNullOrWhiteSpace(query)
                ? AddCatalog.Take(7)
                : AddCatalog.Where(item => item.Matches(query));
        }
    }

    public string AddSearchText
    {
        get => _addSearchText;
        set
        {
            if (!SetProperty(ref _addSearchText, value)) return;
            RaisePropertyChanged(nameof(FilteredAddCatalog));
        }
    }

    public bool IsEditorActive => _isEditorActive;
    public bool IsFirstRunVisible
    {
        get => _isFirstRunVisible;
        private set => SetProperty(ref _isFirstRunVisible, value);
    }
    public bool FirstRunUseStarterTheme
    {
        get => _firstRunUseStarterTheme;
        set => SetProperty(ref _firstRunUseStarterTheme, value);
    }
    public string FirstRunStatus
    {
        get => _firstRunStatus;
        private set => SetProperty(ref _firstRunStatus, value);
    }

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
    public IReadOnlyList<PhotoTransition> PhotoTransitions { get; } =
        Enum.GetValues<PhotoTransition>().Where(value => value != PhotoTransition.KenBurns).ToArray();
    public IReadOnlyList<PhotoCaptionOption> PhotoCaptionModes { get; } =
    [
        new("No caption", PhotoCaptionMode.None),
        new("File name", PhotoCaptionMode.FileName),
        new("Date taken (EXIF)", PhotoCaptionMode.DateTaken),
        new("Location (GPS)", PhotoCaptionMode.Location),
        new("Custom text", PhotoCaptionMode.Custom)
    ];
    public IReadOnlyList<ScreenModeOption> ScreenModes { get; } =
    [
        new("Info Screen", ScreenMode.InfoScreen),
        new("Photo Frame", ScreenMode.PhotoFrame),
        new("Hybrid", ScreenMode.Hybrid)
    ];
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
        set => SelectWidget(value);
    }

    public IReadOnlyList<WidgetModel> SelectedWidgets => Document.EditorWidgets.Where(w => w.IsSelected).ToArray();
    public int SelectedWidgetCount => SelectedWidgets.Count;
    public bool HasMultipleSelection => SelectedWidgetCount > 1;
    public double? AlignmentGuideX => _alignmentGuideX;
    public double? AlignmentGuideY => _alignmentGuideY;

    public PhotoFrameItem? SelectedPhoto
    {
        get => _selectedPhoto;
        set
        {
            if (!SetProperty(ref _selectedPhoto, value)) return;
            RemovePhotoCommand.RaiseCanExecuteChanged();
        }
    }

    public ModeThemeLibraryItem? SelectedModeTheme
    {
        get => _selectedModeTheme;
        set
        {
            if (!SetProperty(ref _selectedModeTheme, value)) return;
            LoadModeThemeCommand.RaiseCanExecuteChanged();
            DeleteModeThemeCommand.RaiseCanExecuteChanged();
        }
    }

    public string ModeThemeTitle => Document.Mode == ScreenMode.Hybrid ? "HYBRID THEMES" : "PHOTO FRAME THEMES";
    public string ModeThemeSummary => Document.Mode == ScreenMode.Hybrid
        ? "Hybrid settings only (.pchybrid). Photos stay on this PC."
        : "Photo Frame settings only (.pcphoto). Photos stay on this PC.";
    public GridLength PropertiesPanelWidth => Document.Mode == ScreenMode.PhotoFrame ? new GridLength(0) : new GridLength(330);

    public bool IsPhotoPlaying => _isPhotoPlaying;
    public string PhotoPlaybackLabel => _isPhotoPlaying ? "Pause" : "Play";
    public string PhotoPositionLabel => Document.PhotoFrame.Photos.Count == 0
        ? "No photos"
        : $"{Document.PhotoFrame.RuntimeCurrentIndex + 1} / {Document.PhotoFrame.Photos.Count}";
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
            DuplicateThemeCommand.RaiseCanExecuteChanged();
            RenameThemeCommand.RaiseCanExecuteChanged();
            DeleteThemeCommand.RaiseCanExecuteChanged();
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
            FirstRunConnectCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CloseToTray
    {
        get => _appSettings.CloseToTray;
        set
        {
            if (_appSettings.CloseToTray == value)
                return;

            _appSettings.CloseToTray = value;
            _settingsService.Save(_appSettings);
            RaisePropertyChanged();
        }
    }

    public bool AutoStartDisplay
    {
        get => _appSettings.AutoStartDisplay;
        set
        {
            if (_appSettings.AutoStartDisplay == value)
                return;

            _appSettings.AutoStartDisplay = value;
            _settingsService.Save(_appSettings);
            RaisePropertyChanged();
        }
    }

    public bool ShowAdvancedSensors
    {
        get => _appSettings.ShowAdvancedSensors;
        set
        {
            if (_appSettings.ShowAdvancedSensors == value)
                return;

            _appSettings.ShowAdvancedSensors = value;
            _settingsService.Save(_appSettings);

            if (value)
            {
                foreach (var source in _advancedSensorSources.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
                    if (!DataSources.Contains(source))
                        DataSources.Add(source);
            }
            else
            {
                foreach (var source in DataSources.Where(x => x.StartsWith("Sensor: ", StringComparison.OrdinalIgnoreCase)).ToArray())
                    DataSources.Remove(source);
            }

            RaisePropertyChanged();
        }
    }

    public bool AdvancedDisplayExpanded
    {
        get => _appSettings.AdvancedDisplayExpanded;
        set
        {
            if (_appSettings.AdvancedDisplayExpanded == value)
                return;

            _appSettings.AdvancedDisplayExpanded = value;
            _settingsService.Save(_appSettings);
            RaisePropertyChanged();
        }
    }

    public bool PositionPanelExpanded { get => _appSettings.PositionPanelExpanded; set => SavePanelState(nameof(PositionPanelExpanded), _appSettings.PositionPanelExpanded, value, v => _appSettings.PositionPanelExpanded = v); }
    public bool DataPanelExpanded { get => _appSettings.DataPanelExpanded; set => SavePanelState(nameof(DataPanelExpanded), _appSettings.DataPanelExpanded, value, v => _appSettings.DataPanelExpanded = v); }
    public bool TypographyPanelExpanded { get => _appSettings.TypographyPanelExpanded; set => SavePanelState(nameof(TypographyPanelExpanded), _appSettings.TypographyPanelExpanded, value, v => _appSettings.TypographyPanelExpanded = v); }
    public bool GraphPanelExpanded { get => _appSettings.GraphPanelExpanded; set => SavePanelState(nameof(GraphPanelExpanded), _appSettings.GraphPanelExpanded, value, v => _appSettings.GraphPanelExpanded = v); }
    public bool GaugePanelExpanded { get => _appSettings.GaugePanelExpanded; set => SavePanelState(nameof(GaugePanelExpanded), _appSettings.GaugePanelExpanded, value, v => _appSettings.GaugePanelExpanded = v); }
    public bool MediaPanelExpanded { get => _appSettings.MediaPanelExpanded; set => SavePanelState(nameof(MediaPanelExpanded), _appSettings.MediaPanelExpanded, value, v => _appSettings.MediaPanelExpanded = v); }
    public bool ShapePanelExpanded { get => _appSettings.ShapePanelExpanded; set => SavePanelState(nameof(ShapePanelExpanded), _appSettings.ShapePanelExpanded, value, v => _appSettings.ShapePanelExpanded = v); }
    public bool ColoursPanelExpanded { get => _appSettings.ColoursPanelExpanded; set => SavePanelState(nameof(ColoursPanelExpanded), _appSettings.ColoursPanelExpanded, value, v => _appSettings.ColoursPanelExpanded = v); }

    private void SavePanelState(string propertyName, bool current, bool value, Action<bool> assign)
    {
        if (current == value) return;
        assign(value);
        _settingsService.Save(_appSettings);
        RaisePropertyChanged(propertyName);
    }

    public string DisplayActionLabel => _deviceService.IsConnected ? "Stop display" : "Start display";

    public double CanvasZoom
    {
        get => Math.Clamp(_appSettings.CanvasZoom, 0.5, 3.0);
        set
        {
            var zoom = Math.Clamp(value, 0.5, 3.0);
            if (Math.Abs(_appSettings.CanvasZoom - zoom) < .001)
                return;

            _appSettings.CanvasZoom = zoom;
            _settingsService.Save(_appSettings);
            RaisePropertyChanged();
        }
    }

    public bool IsDirty => Workspace.IsDirty;
    public string WindowTitle => $"{Document.Name}{(IsDirty ? " *" : string.Empty)} - PC Info Screen Studio";

    public RelayCommand NewCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand SaveAsCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public RelayCommand NudgeWidgetCommand { get; }
    public RelayCommand AlignWidgetCommand { get; }
    public RelayCommand GroupSelectedCommand { get; }
    public RelayCommand UngroupSelectedCommand { get; }
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
    public RelayCommand DuplicateThemeCommand { get; }
    public RelayCommand RenameThemeCommand { get; }
    public RelayCommand DeleteThemeCommand { get; }
    public RelayCommand UpdateWeatherCommand { get; }
    public RelayCommand RestartElevatedCommand { get; }
    public RelayCommand EnableFullSensorsCommand { get; }
    public RelayCommand AddPhotosCommand { get; }
    public RelayCommand AddPhotoFolderCommand { get; }
    public RelayCommand RemovePhotoCommand { get; }
    public RelayCommand PreviousPhotoCommand { get; }
    public RelayCommand TogglePhotoPlaybackCommand { get; }
    public RelayCommand NextPhotoCommand { get; }
    public RelayCommand SaveAlbumPresetCommand { get; }
    public RelayCommand LoadAlbumPresetCommand { get; }
    public RelayCommand ChooseWatchedFolderCommand { get; }
    public RelayCommand SaveModeThemeCommand { get; }
    public RelayCommand LoadModeThemeCommand { get; }
    public RelayCommand DeleteModeThemeCommand { get; }
    public RelayCommand AddCatalogItemCommand { get; }
    public RelayCommand FirstRunConnectCommand { get; }
    public RelayCommand CompleteFirstRunCommand { get; }

    public void SetEditorActive(bool active)
    {
        if (_isEditorActive == active) return;
        _isEditorActive = active;
        RaisePropertyChanged(nameof(IsEditorActive));

        if (active)
        {
            _dataTimer.Start();
            _animationTimer.Start();
            _ = LoadThemeThumbnailsAsync();
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            // Thumbnail bitmaps and the WPF preview are editor-only. Releasing
            // them keeps the tray runtime small while the USB display continues.
            foreach (var theme in Themes)
                theme.Thumbnail = null;
        }

        EditorActivityChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectWidget(WidgetModel? widget, bool additive = false, bool toggle = false)
    {
        var targets = widget is null
            ? Array.Empty<WidgetModel>()
            : widget.GroupId is Guid groupId
                ? Document.EditorWidgets.Where(w => w.GroupId == groupId).ToArray()
                : [widget];

        if (!additive)
        {
            foreach (var item in Document.EditorWidgets)
                item.IsSelected = false;
        }

        if (targets.Length > 0)
        {
            var shouldSelect = !toggle || !targets.All(w => w.IsSelected);
            foreach (var item in targets)
                item.IsSelected = shouldSelect;
        }

        var primary = widget is not null && widget.IsSelected
            ? widget
            : Document.EditorWidgets.LastOrDefault(w => w.IsSelected);
        var primaryChanged = _selectedWidget != primary;
        _selectedWidget = primary;

        if (primaryChanged)
            RaisePropertyChanged(nameof(SelectedWidget));
        RaisePropertyChanged(nameof(SelectedWidgets));
        RaisePropertyChanged(nameof(SelectedWidgetCount));
        RaisePropertyChanged(nameof(HasMultipleSelection));
        RaiseCommandStates();
    }

    public IReadOnlyList<WidgetModel> GetMovementTargets(WidgetModel anchor)
    {
        if (!anchor.IsSelected)
            SelectWidget(anchor);

        return Document.EditorWidgets.Where(w => w.IsSelected && !w.IsLocked).ToArray();
    }

    public void SelectWidgets(IEnumerable<WidgetModel> widgets, bool additive)
    {
        var selected = widgets.ToArray();
        var groupIds = selected.Where(w => w.GroupId is not null).Select(w => w.GroupId).ToHashSet();
        var expanded = Document.EditorWidgets
            .Where(w => selected.Contains(w) || (w.GroupId is not null && groupIds.Contains(w.GroupId)))
            .ToArray();

        if (!additive)
            foreach (var item in Document.EditorWidgets)
                item.IsSelected = false;

        foreach (var item in expanded)
            item.IsSelected = true;

        _selectedWidget = expanded.LastOrDefault() ?? (additive ? Document.EditorWidgets.LastOrDefault(w => w.IsSelected) : null);
        RaisePropertyChanged(nameof(SelectedWidget));
        RaisePropertyChanged(nameof(SelectedWidgets));
        RaisePropertyChanged(nameof(SelectedWidgetCount));
        RaisePropertyChanged(nameof(HasMultipleSelection));
        RaiseCommandStates();
    }

    public (double X, double Y) ApplySmartAlignment(
        WidgetModel anchor,
        double x,
        double y,
        IReadOnlyCollection<WidgetModel> movingWidgets)
    {
        const double threshold = 4;
        var excluded = movingWidgets.Select(w => w.Id).ToHashSet();
        var xTargets = new List<double> { 0, Document.CanvasWidth / 2d, Document.CanvasWidth };
        var yTargets = new List<double> { 0, Document.CanvasHeight / 2d, Document.CanvasHeight };

        foreach (var other in Document.EditorWidgets.Where(w => w.IsVisible && !excluded.Contains(w.Id)))
        {
            xTargets.Add(other.X);
            xTargets.Add(other.X + other.Width / 2d);
            xTargets.Add(other.X + other.Width);
            yTargets.Add(other.Y);
            yTargets.Add(other.Y + other.Height / 2d);
            yTargets.Add(other.Y + other.Height);
        }

        var snappedX = FindGuide(x, anchor.Width, xTargets, threshold);
        var snappedY = FindGuide(y, anchor.Height, yTargets, threshold);
        _alignmentGuideX = snappedX.Guide;
        _alignmentGuideY = snappedY.Guide;
        AlignmentGuidesChanged?.Invoke(this, EventArgs.Empty);
        return (snappedX.Position, snappedY.Position);
    }

    public void ClearAlignmentGuides()
    {
        if (_alignmentGuideX is null && _alignmentGuideY is null) return;
        _alignmentGuideX = null;
        _alignmentGuideY = null;
        AlignmentGuidesChanged?.Invoke(this, EventArgs.Empty);
    }

    private static (double Position, double? Guide) FindGuide(double position, double size, IEnumerable<double> targets, double threshold)
    {
        var points = new[] { position, position + size / 2d, position + size };
        var bestDistance = double.MaxValue;
        var bestOffset = 0d;
        double? guide = null;

        foreach (var target in targets)
        foreach (var point in points)
        {
            var distance = Math.Abs(target - point);
            if (distance > threshold || distance >= bestDistance) continue;
            bestDistance = distance;
            bestOffset = target - point;
            guide = target;
        }

        return (position + bestOffset, guide);
    }

    public void NotifyDesignerChange()
    {
        MarkDirty();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SendLiveFrame(SkiaSharp.SKBitmap bitmap, bool force = false)
    {
        if ((!LivePreview && !force) || !_deviceService.IsConnected) return;
        if (DateTimeOffset.UtcNow < _suspendLiveDisplayUntil) return;

        var next = new PendingDisplayFrame(bitmap.Copy(), Document.DeviceRotation);
        lock (_frameQueueSync)
        {
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
                frame.Bitmap.Dispose();
            }
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
            DeleteRecoveryFile();
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
        AddWidgetToCanvas(type, null);
    }

    private void AddCatalogItem(object? parameter)
    {
        if (parameter is not AddWidgetOption option) return;
        AddWidgetToCanvas(option.Type, option.DataSource);
    }

    private void AddWidgetToCanvas(WidgetType type, string? dataSource)
    {
        var widget = NewWidget(type);
        if (!string.IsNullOrWhiteSpace(dataSource))
        {
            widget.DataSource = dataSource;
            ApplyDataSourceDefaults(widget);
            widget.Name = FriendlyLabel(dataSource);
        }
        Document.EditorWidgets.Insert(0, widget);
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
            Document.EditorWidgets.Insert(0, widget);
            NormalizeZIndices();
            SelectedWidget = widget;
            MarkDirtyAndRefresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not import media", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddPhotos()
    {
        var files = _dialogs.OpenImages();
        if (files.Length > 0)
            AddPhotoFiles(files);
    }

    private void AddPhotoFolder()
    {
        var folder = _dialogs.OpenFolder();
        if (string.IsNullOrWhiteSpace(folder)) return;
        AddPhotoFiles(GetPhotoFiles(folder));
    }

    public void AddPhotoFiles(IEnumerable<string> files)
    {
        var added = new List<PhotoFrameItem>();
        foreach (var path in files.Where(IsSupportedPhoto).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath)) continue;
                if (Document.PhotoFrame.Photos.Any(p => p.AssetId is null && string.Equals(Path.GetFullPath(p.SourcePath), fullPath, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var item = new PhotoFrameItem { SourcePath = fullPath };
                PhotoMetadataService.Populate(item, fullPath);
                Document.PhotoFrame.Photos.Add(item);
                added.Add(item);
            }
            catch (Exception ex)
            {
                DeviceStatus = $"Skipped photo: {Path.GetFileName(path)} ({ex.Message})";
            }
        }

        if (added.Count == 0) return;
        SelectedPhoto = added[0];
        if (Document.PhotoFrame.Photos.Count == added.Count)
            SetCurrentPhoto(0, manual: true);
        MarkDirtyAndRefresh();
        RaisePhotoCommandStates();
    }

    private void RemoveSelectedPhoto()
    {
        var photo = SelectedPhoto;
        if (photo is null) return;
        var current = Document.PhotoFrame.Photos.ElementAtOrDefault(Document.PhotoFrame.RuntimeCurrentIndex);
        var index = Document.PhotoFrame.Photos.IndexOf(photo);
        Document.PhotoFrame.Photos.Remove(photo);
        if (photo.AssetId is Guid assetId && !Document.PhotoFrame.Photos.Any(p => p.AssetId == assetId) && !AllWidgets.Any(w => w.AssetId == assetId))
        {
            var asset = Document.Assets.FirstOrDefault(a => a.Id == assetId);
            if (asset is not null) Document.Assets.Remove(asset);
        }
        SelectedPhoto = Document.PhotoFrame.Photos.Count == 0
            ? null
            : Document.PhotoFrame.Photos[Math.Min(index, Document.PhotoFrame.Photos.Count - 1)];
        Document.PhotoFrame.RuntimeCurrentIndex = current is not null && !ReferenceEquals(current, photo)
            ? Math.Max(0, Document.PhotoFrame.Photos.IndexOf(current))
            : Math.Clamp(index, 0, Math.Max(0, Document.PhotoFrame.Photos.Count - 1));
        Document.PhotoFrame.RuntimePreviousIndex = -1;
        MarkDirtyAndRefresh();
        RaisePhotoCommandStates();
    }

    public void MovePhoto(PhotoFrameItem source, PhotoFrameItem target)
    {
        var current = Document.PhotoFrame.Photos.ElementAtOrDefault(Document.PhotoFrame.RuntimeCurrentIndex);
        var previous = Document.PhotoFrame.Photos.ElementAtOrDefault(Document.PhotoFrame.RuntimePreviousIndex);
        var oldIndex = Document.PhotoFrame.Photos.IndexOf(source);
        var newIndex = Document.PhotoFrame.Photos.IndexOf(target);
        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex) return;
        Document.PhotoFrame.Photos.Move(oldIndex, newIndex);
        Document.PhotoFrame.RuntimeCurrentIndex = current is null ? 0 : Document.PhotoFrame.Photos.IndexOf(current);
        Document.PhotoFrame.RuntimePreviousIndex = previous is null ? -1 : Document.PhotoFrame.Photos.IndexOf(previous);
        SelectedPhoto = source;
        MarkDirtyAndRefresh();
        RaisePropertyChanged(nameof(PhotoPositionLabel));
    }

    public void PreviousPhoto() => MovePhotoBy(-1);
    public void NextPhoto() => MovePhotoBy(1);

    private void MovePhotoBy(int direction)
    {
        var count = Document.PhotoFrame.Photos.Count;
        if (count == 0) return;
        var next = (Document.PhotoFrame.RuntimeCurrentIndex + direction + count) % count;
        SetCurrentPhoto(next, manual: true);
    }

    public void TogglePhotoPlayback()
    {
        _isPhotoPlaying = !_isPhotoPlaying;
        _appSettings.PhotoFramePlaying = _isPhotoPlaying;
        _settingsService.Save(_appSettings);
        _photoStartedAt = DateTimeOffset.UtcNow;
        RaisePropertyChanged(nameof(IsPhotoPlaying));
        RaisePropertyChanged(nameof(PhotoPlaybackLabel));
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SaveAlbumPreset()
    {
        var path = _dialogs.SaveAlbumPreset(Document.Name + " album");
        if (path is null) return;
        try
        {
            _photoAlbumPresetService.Save(Workspace, path);
            DeviceStatus = "Album preset saved";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not save album preset", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadAlbumPreset()
    {
        var path = _dialogs.OpenAlbumPreset();
        if (path is null) return;
        try
        {
            ReplacePhotoFrameSettings(_photoAlbumPresetService.Load(path));
            DeviceStatus = "Album preset loaded";
            MarkDirtyAndRefresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not load album preset", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ChooseWatchedFolder()
    {
        var folder = _dialogs.OpenFolder();
        if (folder is null) return;
        Document.PhotoFrame.WatchedFolder = folder;
        Document.PhotoFrame.WatchFolderEnabled = true;
        AddPhotoFiles(GetPhotoFiles(folder));
        ConfigurePhotoFolderWatcher();
    }

    private static IEnumerable<string> GetPhotoFiles(string folder)
        => Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories).Where(IsSupportedPhoto).OrderBy(Path.GetFileName)
            : [];

    private static bool IsSupportedPhoto(string path)
        => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp";

    private void InitializePhotoFrameRuntime()
    {
        var settings = Document.PhotoFrame;
        settings.EmbedImportedPhotos = false;
        settings.BackgroundMode = PhotoBackgroundMode.SolidColor;
        if (settings.Transition == PhotoTransition.KenBurns)
            settings.Transition = PhotoTransition.Crossfade;
        settings.RuntimeCurrentIndex = Math.Clamp(_appSettings.LastPhotoIndex, 0, Math.Max(0, settings.Photos.Count - 1));
        settings.RuntimePreviousIndex = -1;
        settings.RuntimeTransitionProgress = 1;
        settings.RuntimePhotoProgress = 0;
        settings.RuntimeTransition = ResolveTransition();
        _photoStartedAt = DateTimeOffset.UtcNow;
        _photoTransitionStartedAt = _photoStartedAt;
        SelectedPhoto = settings.Photos.ElementAtOrDefault(settings.RuntimeCurrentIndex);
        UpdateEffectiveScreenMode(force: true);
        ConfigurePhotoFolderWatcher();
        PreloadUpcomingPhoto();
        RaisePhotoCommandStates();
    }

    private bool UpdatePhotoPlayback()
    {
        var settings = Document.PhotoFrame;
        if (settings.Photos.Count == 0) return false;
        var now = DateTimeOffset.UtcNow;
        var duration = TimeSpan.FromSeconds(settings.DefaultDurationSeconds);
        var elapsed = now - _photoStartedAt;

        if (_isPhotoPlaying && elapsed >= duration)
        {
            if (!settings.Loop && !settings.Shuffle && settings.RuntimeCurrentIndex >= settings.Photos.Count - 1)
            {
                _isPhotoPlaying = false;
                RaisePropertyChanged(nameof(IsPhotoPlaying));
                RaisePropertyChanged(nameof(PhotoPlaybackLabel));
            }
            else
            {
                var next = settings.Shuffle && settings.Photos.Count > 1
                    ? NextRandomPhoto(settings.RuntimeCurrentIndex, settings.Photos.Count)
                    : (settings.RuntimeCurrentIndex + 1) % settings.Photos.Count;
                SetCurrentPhoto(next, manual: false);
                return true;
            }
        }

        var transitionDuration = Math.Min(duration.TotalSeconds, settings.TransitionDurationSeconds);
        settings.RuntimeTransitionProgress = transitionDuration <= 0
            ? 1
            : Math.Clamp((now - _photoTransitionStartedAt).TotalSeconds / transitionDuration, 0, 1);
        settings.RuntimePhotoProgress = duration.TotalSeconds <= 0
            ? 1
            : Math.Clamp(elapsed.TotalSeconds / duration.TotalSeconds, 0, 1);
        return settings.RuntimeTransitionProgress < 1;
    }

    private void SetCurrentPhoto(int index, bool manual)
    {
        var settings = Document.PhotoFrame;
        if (settings.Photos.Count == 0) return;
        index = Math.Clamp(index, 0, settings.Photos.Count - 1);
        settings.RuntimePreviousIndex = settings.RuntimeCurrentIndex;
        settings.RuntimeCurrentIndex = index;
        settings.RuntimeTransition = ResolveTransition();
        settings.RuntimeTransitionProgress = settings.RuntimeTransition == PhotoTransition.Instant ? 1 : 0;
        settings.RuntimePhotoProgress = 0;
        _photoStartedAt = DateTimeOffset.UtcNow;
        _photoTransitionStartedAt = _photoStartedAt;
        _appSettings.LastPhotoIndex = index;
        _settingsService.Save(_appSettings);
        SelectedPhoto = settings.Photos[index];
        RaisePropertyChanged(nameof(PhotoPositionLabel));
        ThemeChanged?.Invoke(this, EventArgs.Empty);
        if (LivePreview) RequestLiveFrame?.Invoke(this, EventArgs.Empty);
        PreloadUpcomingPhoto();
    }

    private PhotoTransition ResolveTransition()
    {
        var transition = Document.PhotoFrame.Transition;
        if (transition == PhotoTransition.KenBurns) transition = PhotoTransition.Crossfade;
        if (transition != PhotoTransition.Random) return transition;
        var choices = new[] { PhotoTransition.Crossfade, PhotoTransition.Slide, PhotoTransition.Zoom, PhotoTransition.Instant };
        return choices[_photoRandom.Next(choices.Length)];
    }

    private int NextRandomPhoto(int current, int count)
    {
        var next = _photoRandom.Next(count - 1);
        return next >= current ? next + 1 : next;
    }

    private void PreloadUpcomingPhoto()
    {
        var settings = Document.PhotoFrame;
        if (settings.Photos.Count == 0) return;
        var next = settings.Shuffle && settings.Photos.Count > 1
            ? NextRandomPhoto(settings.RuntimeCurrentIndex, settings.Photos.Count)
            : (settings.RuntimeCurrentIndex + 1) % settings.Photos.Count;
        var workspace = Workspace;
        var item = settings.Photos[next];
        _ = Task.Run(() =>
        {
            try { ThemeRenderer.PreloadPhoto(workspace, item); } catch { }
        });
    }

    private bool UpdateEffectiveScreenMode(bool force = false)
    {
        var next = MapRuntimeMode(Document.Mode);
        if (!force && Document.RuntimeMode == next) return false;
        Document.RuntimeMode = next;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
        if (LivePreview) RequestLiveFrame?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private static RuntimeScreenMode MapRuntimeMode(ScreenMode mode)
        => mode switch
        {
            ScreenMode.PhotoFrame => RuntimeScreenMode.PhotoFrame,
            ScreenMode.Hybrid => RuntimeScreenMode.Hybrid,
            _ => RuntimeScreenMode.InfoScreen
        };

    private void ConfigurePhotoFolderWatcher()
    {
        _photoFolderWatcher?.Dispose();
        _photoFolderWatcher = null;
        var settings = Document.PhotoFrame;
        if (!settings.WatchFolderEnabled || !Directory.Exists(settings.WatchedFolder)) return;

        _photoFolderWatcher = new FileSystemWatcher(settings.WatchedFolder)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };
        _photoFolderWatcher.Created += OnWatchedPhotoChanged;
        _photoFolderWatcher.Renamed += OnWatchedPhotoChanged;
    }

    private void OnWatchedPhotoChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsSupportedPhoto(e.FullPath)) return;
        Application.Current.Dispatcher.BeginInvoke(() => AddPhotoFiles([e.FullPath]));
    }

    private void ReplacePhotoFrameSettings(PhotoFrameSettings settings)
    {
        DetachPhotoFrame(Document.PhotoFrame);
        Document.PhotoFrame = settings;
        AttachPhotoFrame(settings);
        InitializePhotoFrameRuntime();
        RaisePropertyChanged(nameof(Document));
    }

    private void RaisePhotoCommandStates()
    {
        RemovePhotoCommand.RaiseCanExecuteChanged();
        PreviousPhotoCommand.RaiseCanExecuteChanged();
        TogglePhotoPlaybackCommand.RaiseCanExecuteChanged();
        NextPhotoCommand.RaiseCanExecuteChanged();
        SaveAlbumPresetCommand.RaiseCanExecuteChanged();
        RaisePropertyChanged(nameof(PhotoPositionLabel));
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
        var selected = SelectedWidgets.ToArray();
        if (selected.Length == 0) return;
        SelectWidget(null);
        foreach (var widget in selected)
            Document.EditorWidgets.Remove(widget);
        MarkDirty();
    }

    private void DuplicateSelected()
    {
        var selected = SelectedWidgets.OrderBy(w => Document.EditorWidgets.IndexOf(w)).ToArray();
        if (selected.Length == 0) return;

        var newGroupId = selected.Length > 1 ? Guid.NewGuid() : (Guid?)null;
        var clones = selected.Select(w => w.Clone()).ToArray();
        foreach (var clone in clones)
        {
            clone.GroupId = newGroupId;
            Document.EditorWidgets.Insert(0, clone);
        }
        NormalizeZIndices();
        SelectWidget(null);
        foreach (var clone in clones)
            clone.IsSelected = true;
        _selectedWidget = clones.LastOrDefault();
        RaisePropertyChanged(nameof(SelectedWidget));
        RaisePropertyChanged(nameof(SelectedWidgets));
        RaisePropertyChanged(nameof(SelectedWidgetCount));
        RaisePropertyChanged(nameof(HasMultipleSelection));
        RaiseCommandStates();
        MarkDirtyAndRefresh();
    }

    private void NudgeSelected(object? parameter)
    {
        var anchor = SelectedWidget;
        var widgets = SelectedWidgets.Where(w => !w.IsLocked).ToArray();
        if (anchor is null || widgets.Length == 0 || parameter is not string instruction)
            return;

        var parts = instruction.Split(':');
        var direction = parts[0];
        var amount = parts.Length > 1 && double.TryParse(parts[1], out var parsed) ? parsed : 1d;

        var dx = direction == "Left" ? -amount : direction == "Right" ? amount : 0;
        var dy = direction == "Up" ? -amount : direction == "Down" ? amount : 0;
        if (Document.SnapToGrid)
        {
            var spacing = Math.Clamp(Document.GridSize, 2, 100);
            if (dx != 0)
                dx = SnapCoordinate(anchor.X + dx, spacing) - anchor.X;
            if (dy != 0)
                dy = SnapCoordinate(anchor.Y + dy, spacing) - anchor.Y;
        }

        var minX = widgets.Min(w => w.X);
        var maxX = widgets.Max(w => w.X + w.Width);
        var minY = widgets.Min(w => w.Y);
        var maxY = widgets.Max(w => w.Y + w.Height);
        dx = Math.Clamp(dx, -minX, Document.CanvasWidth - maxX);
        dy = Math.Clamp(dy, -minY, Document.CanvasHeight - maxY);
        foreach (var widget in widgets)
        {
            widget.X += dx;
            widget.Y += dy;
        }

        MarkDirtyAndRefresh();
    }

    private void AlignSelected(object? parameter)
    {
        var widgets = SelectedWidgets.Where(w => !w.IsLocked).ToArray();
        if (widgets.Length == 0 || parameter is not string alignment)
            return;

        if (widgets.Length == 1)
        {
            var widget = widgets[0];
            switch (alignment)
            {
                case "Left": widget.X = 0; break;
                case "Center": widget.X = Math.Max(0, (Document.CanvasWidth - widget.Width) / 2); break;
                case "Right": widget.X = Math.Max(0, Document.CanvasWidth - widget.Width); break;
                case "Top": widget.Y = 0; break;
                case "Middle": widget.Y = Math.Max(0, (Document.CanvasHeight - widget.Height) / 2); break;
                case "Bottom": widget.Y = Math.Max(0, Document.CanvasHeight - widget.Height); break;
            }
        }
        else
        {
            var left = widgets.Min(w => w.X);
            var right = widgets.Max(w => w.X + w.Width);
            var top = widgets.Min(w => w.Y);
            var bottom = widgets.Max(w => w.Y + w.Height);
            foreach (var widget in widgets)
            {
                switch (alignment)
                {
                    case "Left": widget.X = left; break;
                    case "Center": widget.X = (left + right - widget.Width) / 2; break;
                    case "Right": widget.X = right - widget.Width; break;
                    case "Top": widget.Y = top; break;
                    case "Middle": widget.Y = (top + bottom - widget.Height) / 2; break;
                    case "Bottom": widget.Y = bottom - widget.Height; break;
                }
            }
        }

        MarkDirtyAndRefresh();
    }

    private void GroupSelected()
    {
        var selected = SelectedWidgets.ToArray();
        if (selected.Length < 2) return;
        var groupId = Guid.NewGuid();
        foreach (var widget in selected)
            widget.GroupId = groupId;
        MarkDirtyAndRefresh();
        RaiseCommandStates();
    }

    private void UngroupSelected()
    {
        var selected = SelectedWidgets.ToArray();
        if (selected.Length == 0) return;
        foreach (var widget in selected)
            widget.GroupId = null;
        MarkDirtyAndRefresh();
        RaiseCommandStates();
    }

    private static double SnapCoordinate(double value, double spacing)
        => Math.Round(value / spacing, MidpointRounding.AwayFromZero) * spacing;

    private void MoveLayer(int delta)
    {
        if (SelectedWidget is null) return;

        var current = Document.EditorWidgets.IndexOf(SelectedWidget);
        if (current < 0) return;

        var target = Math.Clamp(current - delta, 0, Document.EditorWidgets.Count - 1);
        if (target == current) return;

        Document.EditorWidgets.Move(current, target);
        NormalizeZIndices();
        MarkDirtyAndRefresh();
    }

    private void NormalizeZIndices()
    {
        for (var i = 0; i < Document.EditorWidgets.Count; i++)
            Document.EditorWidgets[i].ZIndex = Document.EditorWidgets.Count - 1 - i;
    }

    private void ToggleOrientation()
    {
        var oldW = Document.CanvasWidth;
        var oldH = Document.CanvasHeight;
        Document.Orientation = Document.Orientation == ThemeOrientation.Landscape ? ThemeOrientation.Portrait : ThemeOrientation.Landscape;
        var scaleX = Document.CanvasWidth / (double)oldW;
        var scaleY = Document.CanvasHeight / (double)oldH;
        foreach (var w in AllWidgets)
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

    private async Task DetectAndConnectFirstRunAsync()
    {
        if (_deviceService.IsConnected)
        {
            FirstRunStatus = "Screen is connected. Choose a mode and finish setup.";
            return;
        }

        DetectScreen();
        if (string.IsNullOrWhiteSpace(SelectedPort))
        {
            FirstRunStatus = "No screen was found. Check the USB cable; you can finish setup and connect later.";
            return;
        }

        FirstRunStatus = $"Screen found on {SelectedPort}. Connecting…";
        await ConnectOrDisconnectAsync();
        FirstRunStatus = _deviceService.IsConnected
            ? "Screen connected. Choose a mode and finish setup."
            : "The screen could not be connected. You can finish setup and retry from Device Settings.";
    }

    private void CompleteFirstRun()
    {
        if (FirstRunUseStarterTheme)
            CreateStarterLayout();
        else
            CreateBlankLayout();

        _appSettings.FirstRunCompleted = true;
        _settingsService.Save(_appSettings);
        IsFirstRunVisible = false;
    }

    private async Task ConnectOrDisconnectAsync()
    {
        if (IsDeviceBusy) return;

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
                BenchmarkCommand.RaiseCanExecuteChanged();
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedPort))
            DetectScreen();

        if (string.IsNullOrWhiteSpace(SelectedPort))
        {
            MessageBox.Show("No compatible serial display was found.", "Start display", MessageBoxButton.OK, MessageBoxImage.Information);
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

            LivePreview = true;
            DeviceStatus = BuildConnectionStatus();
            RaisePropertyChanged(nameof(DisplayActionLabel));
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
            RaisePropertyChanged(nameof(DisplayActionLabel));
            BenchmarkCommand.RaiseCanExecuteChanged();
        }
    }

    public void StartAutoDisplayIfEnabled()
    {
        if (!AutoStartDisplay || _deviceService.IsConnected || IsDeviceBusy)
            return;

        DetectScreen();
        if (!string.IsNullOrWhiteSpace(SelectedPort))
            _ = ConnectOrDisconnectAsync();
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

        _ = LoadThemeThumbnailsAsync();
    }

    private void RefreshModeThemes()
    {
        var previousPath = SelectedModeTheme?.FilePath;
        ModeThemes.Clear();
        foreach (var theme in _modeThemeService.GetThemes(Document.Mode))
            ModeThemes.Add(theme);
        SelectedModeTheme = ModeThemes.FirstOrDefault(theme =>
                                string.Equals(theme.FilePath, previousPath, StringComparison.OrdinalIgnoreCase))
                            ?? ModeThemes.FirstOrDefault();
    }

    private void SaveModeTheme()
    {
        if (Document.Mode == ScreenMode.InfoScreen) return;
        var label = Document.Mode == ScreenMode.Hybrid ? "Hybrid theme name" : "Photo Frame theme name";
        var name = TextPromptDialog.Show(
            Application.Current.MainWindow,
            "Save settings theme",
            label,
            SelectedModeTheme?.DisplayName ?? "My settings");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            var path = _modeThemeService.Save(Document, Document.Mode, name);
            RefreshModeThemes();
            SelectedModeTheme = ModeThemes.FirstOrDefault(theme =>
                string.Equals(theme.FilePath, path, StringComparison.OrdinalIgnoreCase));
            DeviceStatus = $"{Document.Mode} settings theme saved";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not save settings theme", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LoadModeTheme()
    {
        if (SelectedModeTheme is null) return;
        try
        {
            var preset = _modeThemeService.Load(SelectedModeTheme.FilePath);
            if (preset.Mode != Document.Mode)
                throw new InvalidDataException("This settings theme belongs to a different screen mode.");
            ApplyGlobalPhotoSettings(preset.PhotoFrame);
            if (Document.Mode == ScreenMode.Hybrid)
            {
                Document.HybridWidgets.Clear();
                foreach (var widget in preset.HybridWidgets)
                    Document.HybridWidgets.Add(widget.Clone());
                SelectedWidget = Document.HybridWidgets.OrderBy(widget => widget.ZIndex).FirstOrDefault();
            }
            MarkDirtyAndRefresh();
            DeviceStatus = $"{SelectedModeTheme.DisplayName} loaded";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not load settings theme", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DeleteModeTheme()
    {
        if (SelectedModeTheme is null) return;
        var answer = MessageBox.Show(
            $"Delete '{SelectedModeTheme.DisplayName}'?",
            "Delete settings theme",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        _modeThemeService.Delete(SelectedModeTheme.FilePath);
        RefreshModeThemes();
    }

    private void ApplyGlobalPhotoSettings(PhotoFrameSettings source)
    {
        var target = Document.PhotoFrame;
        target.DefaultDurationSeconds = source.DefaultDurationSeconds;
        target.TransitionDurationSeconds = source.TransitionDurationSeconds;
        target.Transition = source.Transition == PhotoTransition.KenBurns ? PhotoTransition.Crossfade : source.Transition;
        target.Fit = source.Fit;
        target.Loop = source.Loop;
        target.Shuffle = source.Shuffle;
        target.EmbedImportedPhotos = false;
        target.BackgroundMode = PhotoBackgroundMode.SolidColor;
        target.BackgroundColor = source.BackgroundColor;
        target.ShowCaptions = source.ShowCaptions;
        target.CaptionFontSize = source.CaptionFontSize;
        target.CaptionColor = source.CaptionColor;
        target.CaptionOutlineColor = source.CaptionOutlineColor;
        target.CaptionOutlineThickness = source.CaptionOutlineThickness;
        target.CaptionMode = source.CaptionMode;
        target.CustomCaption = source.CustomCaption;
        target.WatchFolderEnabled = source.WatchFolderEnabled;
        target.WatchedFolder = source.WatchedFolder;
    }

    private async Task LoadThemeThumbnailsAsync()
    {
        foreach (var item in Themes.Where(t => !t.IsBuiltIn && !string.IsNullOrWhiteSpace(t.FilePath)).ToArray())
        {
            try
            {
                using var workspace = await _packageService.LoadAsync(item.FilePath!);
                var renderer = new ThemeRenderer();
                using var bitmap = renderer.Render(workspace);
                var source = BitmapSource.Create(
                    bitmap.Width,
                    bitmap.Height,
                    96,
                    96,
                    PixelFormats.Bgra32,
                    null,
                    bitmap.GetPixels(),
                    bitmap.RowBytes * bitmap.Height,
                    bitmap.RowBytes);
                source.Freeze();
                item.Thumbnail = source;
            }
            catch
            {
                // A damaged theme remains listed and can still be deleted or replaced.
            }
        }
    }

    private void DuplicateSelectedTheme()
    {
        if (SelectedTheme?.FilePath is not string path)
            return;

        try
        {
            var duplicate = _themeLibrary.Duplicate(path);
            RefreshThemes();
            SelectedTheme = Themes.FirstOrDefault(t => string.Equals(t.FilePath, duplicate, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not duplicate theme", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RenameSelectedTheme()
    {
        if (SelectedTheme?.FilePath is not string path)
            return;

        var name = TextPromptDialog.Show(
            Application.Current.MainWindow,
            "Rename theme",
            "New theme name",
            Path.GetFileNameWithoutExtension(path));
        if (string.IsNullOrWhiteSpace(name))
            return;

        try
        {
            var renamed = _themeLibrary.Rename(path, name);
            if (string.Equals(Workspace.FilePath, path, StringComparison.OrdinalIgnoreCase))
                Workspace.FilePath = renamed;

            RefreshThemes();
            SelectedTheme = Themes.FirstOrDefault(t => string.Equals(t.FilePath, renamed, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not rename theme", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DeleteSelectedTheme()
    {
        if (SelectedTheme?.FilePath is not string path)
            return;

        var answer = MessageBox.Show(
            $"Delete '{SelectedTheme.DisplayName}' from the theme library?",
            "Delete theme",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            _themeLibrary.Delete(path);
            if (string.Equals(Workspace.FilePath, path, StringComparison.OrdinalIgnoreCase))
                Workspace.FilePath = null;
            RefreshThemes();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not delete theme", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
        ThemeRenderer.ClearCaches();
        _workspace = workspace;
        AttachWorkspace(_workspace);
        FontAssets.Clear();
        foreach (var font in Document.Assets.Where(a => a.Kind == ThemeAssetKind.Font)) FontAssets.Add(font);
        SelectedWidget = Document.EditorWidgets.OrderBy(w => w.ZIndex).FirstOrDefault();
        InitializePhotoFrameRuntime();
        RaisePropertyChanged(nameof(Workspace));
        RaisePropertyChanged(nameof(Document));
        RaisePropertyChanged(nameof(IsDirty));
        RaisePropertyChanged(nameof(WindowTitle));
        ThemeChanged?.Invoke(this, EventArgs.Empty);
        InitializeHistory();
    }

    private void AttachWorkspace(ThemeWorkspace workspace)
    {
        workspace.Document.PropertyChanged += OnDocumentPropertyChanged;
        workspace.Document.Widgets.CollectionChanged += OnWidgetsChanged;
        workspace.Document.HybridWidgets.CollectionChanged += OnWidgetsChanged;
        workspace.Document.Assets.CollectionChanged += OnAssetsChanged;
        foreach (var w in workspace.Document.Widgets) w.PropertyChanged += OnWidgetPropertyChanged;
        foreach (var w in workspace.Document.HybridWidgets) w.PropertyChanged += OnWidgetPropertyChanged;
        AttachPhotoFrame(workspace.Document.PhotoFrame);
    }

    private void DetachWorkspace(ThemeWorkspace workspace)
    {
        workspace.Document.PropertyChanged -= OnDocumentPropertyChanged;
        workspace.Document.Widgets.CollectionChanged -= OnWidgetsChanged;
        workspace.Document.HybridWidgets.CollectionChanged -= OnWidgetsChanged;
        workspace.Document.Assets.CollectionChanged -= OnAssetsChanged;
        foreach (var w in workspace.Document.Widgets) w.PropertyChanged -= OnWidgetPropertyChanged;
        foreach (var w in workspace.Document.HybridWidgets) w.PropertyChanged -= OnWidgetPropertyChanged;
        DetachPhotoFrame(workspace.Document.PhotoFrame);
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ThemeDocument.Mode))
        {
            UpdateEffectiveScreenMode(force: true);
            SelectWidget(Document.EditorWidgets.OrderBy(widget => widget.ZIndex).FirstOrDefault());
            RefreshModeThemes();
            RaisePropertyChanged(nameof(ModeThemeTitle));
            RaisePropertyChanged(nameof(ModeThemeSummary));
            RaisePropertyChanged(nameof(PropertiesPanelWidth));
            SaveModeThemeCommand.RaiseCanExecuteChanged();
        }
        MarkDirtyAndRefresh();

        if (_deviceService.IsConnected &&
            e.PropertyName is nameof(ThemeDocument.Orientation) or nameof(ThemeDocument.DeviceRotation))
        {
            _ = ApplyDeviceOrientationAsync();
        }
    }

    private void AttachPhotoFrame(PhotoFrameSettings settings)
    {
        settings.PropertyChanged += OnPhotoFramePropertyChanged;
        settings.Photos.CollectionChanged += OnPhotosChanged;
        foreach (var photo in settings.Photos)
            photo.PropertyChanged += OnPhotoPropertyChanged;
    }

    private void DetachPhotoFrame(PhotoFrameSettings settings)
    {
        settings.PropertyChanged -= OnPhotoFramePropertyChanged;
        settings.Photos.CollectionChanged -= OnPhotosChanged;
        foreach (var photo in settings.Photos)
            photo.PropertyChanged -= OnPhotoPropertyChanged;
    }

    private void OnPhotoFramePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PhotoFrameSettings.WatchFolderEnabled) or nameof(PhotoFrameSettings.WatchedFolder))
            ConfigurePhotoFolderWatcher();
        MarkDirtyAndRefresh();
    }

    private void OnPhotoPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => MarkDirtyAndRefresh();

    private void OnPhotosChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (PhotoFrameItem photo in e.OldItems)
                photo.PropertyChanged -= OnPhotoPropertyChanged;
        if (e.NewItems is not null)
            foreach (PhotoFrameItem photo in e.NewItems)
                photo.PropertyChanged += OnPhotoPropertyChanged;
        RaisePhotoCommandStates();
        MarkDirtyAndRefresh();
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

                if (pair.Key.StartsWith("Sensor: ", StringComparison.OrdinalIgnoreCase))
                {
                    _advancedSensorSources.Add(pair.Key);
                    if (ShowAdvancedSensors && !DataSources.Contains(pair.Key))
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

            foreach (var widget in AllWidgets)
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

            if (IsEditorActive)
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
                "CPU Package temperature, CPU Package power and many motherboard sensors need the signed PawnIO hardware-access driver used by LibreHardwareMonitor.\n\nRunning PC Info Screen Studio as Administrator by itself does NOT install this driver.\n\nInstall the bundled PawnIO hardware driver now?",
                "Hardware sensors",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (answer != MessageBoxResult.Yes)
                return;

            try
            {
                var bundledInstaller = Path.Combine(
                    AppContext.BaseDirectory,
                    "Prerequisites",
                    "PawnIO_setup.exe");

                ProcessStartInfo startInfo;

                if (File.Exists(bundledInstaller))
                {
                    startInfo = new ProcessStartInfo
                    {
                        FileName = bundledInstaller,
                        Arguments = "-install -silent",
                        UseShellExecute = true,
                        Verb = "runas",
                        WorkingDirectory = Path.GetDirectoryName(bundledInstaller) ?? AppContext.BaseDirectory
                    };
                }
                else
                {
                    startInfo = new ProcessStartInfo
                    {
                        FileName = "winget",
                        Arguments = "install --exact --id namazso.PawnIO --accept-package-agreements --accept-source-agreements",
                        UseShellExecute = true,
                        Verb = "runas",
                        WorkingDirectory = AppContext.BaseDirectory
                    };
                }

                var process = Process.Start(startInfo);

                if (process is null)
                    throw new InvalidOperationException("PawnIO setup could not be started.");

                await process.WaitForExitAsync();

                // 3010 = installation succeeded, restart required.
                // 1641 = installation succeeded and restart was initiated.
                if (process.ExitCode is not (0 or 3010 or 1641))
                {
                    MessageBox.Show(
                        $"PawnIO setup returned exit code {process.ExitCode}.\n\n" +
                        "You can retry from Hardware sensors..., or install it manually with:\n" +
                        "winget install --exact --id namazso.PawnIO",
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
                    ex.Message + "\n\nIf the bundled installer is unavailable, you can install PawnIO manually with:\nwinget install --exact --id namazso.PawnIO",
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

        var sensorCheck = await Task.Run(_hardwareMetrics.Sample);

        static string ShowMetric(IReadOnlyDictionary<string, MetricValue> values, string key)
        {
            if (!values.TryGetValue(key, out var metric) || metric.Numeric is not double number)
                return "N/A";

            return number.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) +
                   (metric.Unit ?? string.Empty);
        }

        MessageBox.Show(
            "Full sensor access is enabled.\n\n" +
            $"CPU temperature: {ShowMetric(sensorCheck, "CPU.Temperature")}\n" +
            $"CPU power: {ShowMetric(sensorCheck, "CPU.Power")}\n" +
            $"GPU temperature: {ShowMetric(sensorCheck, "GPU.Temperature")}\n" +
            $"GPU VRAM: {ShowMetric(sensorCheck, "GPU.VRAM")}\n" +
            $"Disk temperature: {ShowMetric(sensorCheck, "Disk.Temperature")}\n\n" +
            _hardwareMetrics.Status +
            "\n\nTurn on 'Live data' to feed these values into widgets.",
            "Hardware sensor check",
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
        foreach (var widget in AllWidgets)
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
        ScheduleHistoryCapture();
    }

    private void InitializeHistory()
    {
        _historyTimer?.Stop();
        _history.Clear();
        _history.Add(_historyService.Capture(Document));
        _historyIndex = 0;
        RaiseHistoryCommandStates();
    }

    private void ScheduleHistoryCapture()
    {
        if (_suppressHistory || _suppressDirty || _historyTimer is null)
            return;

        _historyTimer.Stop();
        _historyTimer.Start();
    }

    private void CaptureHistoryNow()
    {
        if (_suppressHistory)
            return;

        var snapshot = _historyService.Capture(Document);
        if (_historyIndex >= 0 && _historyIndex < _history.Count &&
            string.Equals(_history[_historyIndex], snapshot, StringComparison.Ordinal))
            return;

        if (_historyIndex < _history.Count - 1)
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);

        _history.Add(snapshot);
        if (_history.Count > 60)
            _history.RemoveAt(0);

        _historyIndex = _history.Count - 1;
        RaiseHistoryCommandStates();
    }

    private void Undo()
    {
        CaptureHistoryNow();
        if (_historyIndex <= 0)
            return;

        _historyIndex--;
        ApplyHistorySnapshot(_history[_historyIndex]);
    }

    private void Redo()
    {
        if (_historyIndex < 0 || _historyIndex >= _history.Count - 1)
            return;

        _historyIndex++;
        ApplyHistorySnapshot(_history[_historyIndex]);
    }

    private void ApplyHistorySnapshot(string snapshot)
    {
        var restored = _historyService.Restore(snapshot);
        var selectedId = SelectedWidget?.Id;
        var assetPaths = Document.Assets.ToDictionary(a => a.Id, a => a.LocalPath);

        _suppressHistory = true;
        _suppressDirty = true;
        try
        {
            DetachWorkspace(_workspace);

            Document.FormatVersion = restored.FormatVersion;
            Document.MinimumAppVersion = restored.MinimumAppVersion;
            Document.Name = restored.Name;
            Document.Author = restored.Author;
            Document.Description = restored.Description;
            Document.Orientation = restored.Orientation;
            Document.CanvasWidth = restored.CanvasWidth;
            Document.CanvasHeight = restored.CanvasHeight;
            Document.DeviceRotation = restored.DeviceRotation;
            Document.BackgroundColor = restored.BackgroundColor;
            Document.EditorGridVisible = restored.EditorGridVisible;
            Document.SnapToGrid = restored.SnapToGrid;
            Document.GridSize = restored.GridSize;
            Document.Mode = restored.Mode;
            Document.PhotoFrame = restored.PhotoFrame;

            Document.Widgets.Clear();
            foreach (var widget in restored.Widgets)
                Document.Widgets.Add(widget);

            Document.HybridWidgets.Clear();
            foreach (var widget in restored.HybridWidgets)
                Document.HybridWidgets.Add(widget);

            Document.Assets.Clear();
            foreach (var asset in restored.Assets)
            {
                if (assetPaths.TryGetValue(asset.Id, out var localPath))
                    asset.LocalPath = localPath;
                Document.Assets.Add(asset);
            }

            AttachWorkspace(_workspace);
            FontAssets.Clear();
            foreach (var font in Document.Assets.Where(a => a.Kind == ThemeAssetKind.Font))
                FontAssets.Add(font);

            InitializePhotoFrameRuntime();
            SelectedWidget = Document.EditorWidgets.FirstOrDefault(w => w.Id == selectedId)
                             ?? Document.EditorWidgets.FirstOrDefault();
            Workspace.IsDirty = true;
        }
        finally
        {
            _suppressDirty = false;
            _suppressHistory = false;
        }

        RaisePropertyChanged(nameof(IsDirty));
        RaisePropertyChanged(nameof(WindowTitle));
        RaiseHistoryCommandStates();
        ThemeRenderer.ClearCaches();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
        if (LivePreview)
            RequestLiveFrame?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseHistoryCommandStates()
    {
        UndoCommand?.RaiseCanExecuteChanged();
        RedoCommand?.RaiseCanExecuteChanged();
    }

    private async Task SaveRecoveryAsync()
    {
        if (!Workspace.IsDirty || _recoveryBusy)
            return;

        _recoveryBusy = true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_recoveryPath)!);
            await _packageService.SaveCopyAsync(Workspace, _recoveryPath);
        }
        catch
        {
            // Recovery is best-effort and must never interrupt editing.
        }
        finally
        {
            _recoveryBusy = false;
        }
    }

    public async Task RecoverIfAvailableAsync()
    {
        if (!File.Exists(_recoveryPath))
            return;

        var answer = MessageBox.Show(
            "PC Info Screen Studio found an unsaved theme from the previous session.\n\nRecover it now?",
            "Recover unsaved theme",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            DeleteRecoveryFile();
            return;
        }

        try
        {
            var recovered = await _packageService.LoadAsync(_recoveryPath);
            recovered.FilePath = null;
            recovered.IsDirty = true;
            ReplaceWorkspace(recovered);
            Workspace.IsDirty = true;
            RaisePropertyChanged(nameof(IsDirty));
            RaisePropertyChanged(nameof(WindowTitle));
            DeleteRecoveryFile();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not recover theme", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DeleteRecoveryFile()
    {
        try
        {
            if (File.Exists(_recoveryPath))
                File.Delete(_recoveryPath);
        }
        catch
        {
        }
    }

    private void RaiseCommandStates()
    {
        DeleteWidgetCommand.RaiseCanExecuteChanged();
        DuplicateWidgetCommand.RaiseCanExecuteChanged();
        MoveLayerUpCommand.RaiseCanExecuteChanged();
        MoveLayerDownCommand.RaiseCanExecuteChanged();
        NudgeWidgetCommand.RaiseCanExecuteChanged();
        AlignWidgetCommand.RaiseCanExecuteChanged();
        GroupSelectedCommand.RaiseCanExecuteChanged();
        UngroupSelectedCommand.RaiseCanExecuteChanged();
    }

    private bool ConfirmDiscardIfNeeded()
    {
        if (!Workspace.IsDirty) return true;
        var result = MessageBox.Show("This theme has unsaved changes. Continue and discard them?", "Unsaved changes", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }

    private sealed record PendingDisplayFrame(SkiaSharp.SKBitmap Bitmap, DeviceRotation Rotation);

    public sealed record RotationOption(string Label, DeviceRotation Value);
    public sealed record ScreenModeOption(string Label, ScreenMode Value);
    public sealed record PhotoCaptionOption(string Label, PhotoCaptionMode Value);
    public sealed record AddWidgetOption(string Label, string Description, WidgetType Type, string? DataSource, string SearchTerms)
    {
        public bool Matches(string query)
            => (Label + " " + Description + " " + SearchTerms + " " + DataSource)
                .Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    public void Dispose()
    {
        _dataTimer.Stop();
        _animationTimer.Stop();
        _historyTimer.Stop();
        _recoveryTimer.Stop();
        DeleteRecoveryFile();
        _weatherMetrics.Dispose();
        _hardwareMetrics.Dispose();
        _photoFolderWatcher?.Dispose();
        lock (_frameQueueSync)
        {
            _pendingFrame?.Bitmap.Dispose();
            _pendingFrame = null;
        }
        ThemeRenderer.ClearCaches();
        _deviceService.Dispose();
        DetachWorkspace(_workspace);
        _workspace.Dispose();
    }
}
