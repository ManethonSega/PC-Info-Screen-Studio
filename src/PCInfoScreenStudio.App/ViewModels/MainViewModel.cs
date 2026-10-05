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
using PCInfoScreenStudio.Controllers;

namespace PCInfoScreenStudio.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly SettingsController _settings;
    private readonly DeviceController _device;
    private readonly EditorController _editor;
    private readonly PhotoPlaybackController _photos;
    private readonly ThemePackageService _packageService = new();
    private readonly AssetImportService _assetService = new();
    private readonly FileDialogService _dialogs = new();
    private readonly ThemeLibraryService _themeLibrary = new();
    private readonly SystemMetricsService _systemMetrics = new();
    private readonly HardwareMetricsService _hardwareMetrics = new();
    private readonly WeatherMetricsService _weatherMetrics = new();
    private readonly ModeThemeService _modeThemeService = new();
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
    private ThemeLibraryItem? _selectedTheme;
    private string _weatherCity = string.Empty;
    private string _weatherStatus = "Weather city not configured.";
    private string _hardwareStatus = "Hardware sensors not initialized.";
    private bool _suppressDirty;
    private bool _suppressHistory;
    private bool _recoveryBusy;
    private int _historyIndex = -1;
    private ModeThemeLibraryItem? _selectedModeTheme;
    private readonly HashSet<string> _advancedSensorSources = new(StringComparer.OrdinalIgnoreCase);
    private int _dataSampleBusy;
    private bool _isEditorActive = true;
    private bool _isFirstRunVisible;
    private bool _firstRunUseStarterTheme = true;
    private string _firstRunStatus = "Connect your screen now, or finish setup and connect later.";
    private string _addSearchText = string.Empty;
    private bool _isLiveMode;

    public event EventHandler? AlignmentGuidesChanged;

    public MainViewModel() : this(new SettingsController()) { }

    public MainViewModel(SettingsController settings)
    {
        _settings = settings;
        _weatherCity = _settings.WeatherCity;
        _workspace = _packageService.CreateNewWorkspace();
        _device = new DeviceController(() => Document, _settings);
        _editor = new EditorController(() => Document);
        _photos = new PhotoPlaybackController(() => Workspace, _settings);
        AttachControllers();
        Themes = [];
        ModeThemes = [];
        FontAssets = [];

        NewCommand = new RelayCommand(NewTheme, () => IsEditorPage);
        OpenCommand = new RelayCommand(async () => await OpenThemeAsync(), () => IsEditorPage);
        SaveCommand = new RelayCommand(async () => await SaveAsync(false), () => IsEditorPage);
        SaveAsCommand = new RelayCommand(async () => await SaveAsync(true), () => IsEditorPage);
        UndoCommand = new RelayCommand(Undo, () => IsEditorPage && _historyIndex > 0);
        RedoCommand = new RelayCommand(Redo, () => IsEditorPage && _historyIndex >= 0 && _historyIndex < _history.Count - 1);
        NudgeWidgetCommand = new RelayCommand(NudgeSelected, _ => IsEditorPage && SelectedWidgets.Any(w => !w.IsLocked));
        AlignWidgetCommand = new RelayCommand(AlignSelected, _ => IsEditorPage && SelectedWidgets.Any(w => !w.IsLocked));
        GroupSelectedCommand = new RelayCommand(GroupSelected, () => IsEditorPage && SelectedWidgets.Count >= 2);
        UngroupSelectedCommand = new RelayCommand(UngroupSelected, () => IsEditorPage && SelectedWidgets.Any(w => w.GroupId is not null));
        AddWidgetCommand = new RelayCommand(AddWidget);
        DeleteWidgetCommand = new RelayCommand(DeleteSelected, () => IsEditorPage && SelectedWidget is not null);
        DuplicateWidgetCommand = new RelayCommand(DuplicateSelected, () => IsEditorPage && SelectedWidget is not null);
        MoveLayerUpCommand = new RelayCommand(() => MoveLayer(1), () => IsEditorPage && SelectedWidget is not null);
        MoveLayerDownCommand = new RelayCommand(() => MoveLayer(-1), () => IsEditorPage && SelectedWidget is not null);
        ImportImageCommand = new RelayCommand(() => ImportMedia(ThemeAssetKind.Image));
        ImportGifCommand = new RelayCommand(() => ImportMedia(ThemeAssetKind.Gif));
        ImportVideoCommand = new RelayCommand(() => ImportMedia(ThemeAssetKind.Video));
        ImportFontCommand = new RelayCommand(ImportFont);
        ToggleOrientationCommand = new RelayCommand(ToggleOrientation);
        RotateDeviceCommand = new RelayCommand(RotateDevice);
        RefreshPortsCommand = new RelayCommand(RefreshPorts);
        DetectScreenCommand = new RelayCommand(DetectScreen, () => !IsDeviceBusy);
        ConnectCommand = new RelayCommand(() => _ = ConnectOrDisconnectAsync(), () => !IsDeviceBusy);
        TestScreenCommand = new RelayCommand(() => _ = TestScreenAsync(), () => _device.IsConnected && !IsDeviceBusy);
        BenchmarkCommand = new RelayCommand(() => _ = RunBenchmarkAsync(), () => _device.IsConnected && !IsDeviceBusy);
        RefreshThemesCommand = new RelayCommand(() => { if (Document.Mode == ScreenMode.InfoScreen) RefreshThemes(); else RefreshModeThemes(); });
        LoadThemeCommand = new RelayCommand(() => _ = LoadSelectedThemeAsync(), () => IsEditorPage && ActiveThemeSelection is not null);
        DuplicateThemeCommand = new RelayCommand(DuplicateSelectedTheme, () => CanManageActiveTheme);
        RenameThemeCommand = new RelayCommand(RenameSelectedTheme, () => CanManageActiveTheme);
        DeleteThemeCommand = new RelayCommand(DeleteSelectedTheme, () => CanManageActiveTheme);
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
        ShowSetupAssistantCommand = new RelayCommand(ShowSetupAssistant);
        ToggleEditorModeCommand = new RelayCommand(ToggleEditorMode, () => IsEditorPage);
        ShowEditorPageCommand = new RelayCommand(() => ShowPage(false));
        ShowHardwarePageCommand = new RelayCommand(() => ShowPage(true));

        _dataTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _dataTimer.Tick += async (_, _) =>
        {
            if (!IsEditorActive && !LivePreview)
                return;
            await RefreshRuntimeDataAsync();
        };
        _dataTimer.Start();

        _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _animationTimer.Tick += (_, _) =>
        {
            if (!IsCanvasActive && !LivePreview)
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
            if (IsCanvasActive)
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
        _isFirstRunVisible = !_settings.FirstRunCompleted;
    }

    public event EventHandler? ThemeChanged;
    public event EventHandler? RequestLiveFrame;
    public event EventHandler? EditorActivityChanged;

    public ThemeWorkspace Workspace => _workspace;
    public ThemeDocument Document => _workspace.Document;
    private IEnumerable<WidgetModel> RuntimeWidgets => Document.RuntimeMode == RuntimeScreenMode.Hybrid ? Document.HybridWidgets : Document.Widgets;
    private IEnumerable<WidgetModel> AllWidgets => Document.Widgets.Concat(Document.HybridWidgets);
    public ObservableCollection<SerialPortOption> Ports => _device.Ports;
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
                ? Array.Empty<AddWidgetOption>()
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
        get => _editor.SelectedWidget;
        set => _editor.SelectedWidget = value;
    }

    public IReadOnlyList<WidgetModel> SelectedWidgets => _editor.SelectedWidgets;
    public int SelectedWidgetCount => SelectedWidgets.Count;
    public bool HasMultipleSelection => SelectedWidgetCount > 1;
    public double? AlignmentGuideX => _editor.AlignmentGuideX;
    public double? AlignmentGuideY => _editor.AlignmentGuideY;

    public PhotoFrameItem? SelectedPhoto
    {
        get => _photos.SelectedPhoto;
        set => _photos.SelectedPhoto = value;
    }

    public ModeThemeLibraryItem? SelectedModeTheme
    {
        get => _selectedModeTheme;
        set
        {
            if (!SetProperty(ref _selectedModeTheme, value)) return;
            LoadModeThemeCommand.RaiseCanExecuteChanged();
            DeleteModeThemeCommand.RaiseCanExecuteChanged();
            NotifyThemeState();
        }
    }

    public string ModeThemeTitle => Document.Mode == ScreenMode.Hybrid ? "HYBRID THEMES" : "PHOTO FRAME THEMES";
    public string ModeThemeSummary => Document.Mode == ScreenMode.Hybrid
        ? "Hybrid settings only (.pchybrid). Photos stay on this PC."
        : "Photo Frame settings only (.pcphoto). Photos stay on this PC.";
    public GridLength LeftPanelWidth => IsLiveMode ? new GridLength(0) : new GridLength(320);
    public GridLength PropertiesPanelWidth => IsLiveMode || Document.Mode == ScreenMode.PhotoFrame ? new GridLength(0) : new GridLength(330);
    public GridLength EditorTopBarHeight => IsLiveMode ? new GridLength(0) : new GridLength(42);
    public GridLength EditorBottomBarHeight => IsLiveMode ? new GridLength(0) : new GridLength(32);
    public Visibility EditorChromeVisibility => IsLiveMode ? Visibility.Collapsed : Visibility.Visible;
    public bool IsLiveMode
    {
        get => _isLiveMode;
        private set
        {
            if (!SetProperty(ref _isLiveMode, value)) return;
            RaisePropertyChanged(nameof(LeftPanelWidth));
            RaisePropertyChanged(nameof(PropertiesPanelWidth));
            RaisePropertyChanged(nameof(EditorChromeVisibility));
            RaisePropertyChanged(nameof(EditorTopBarHeight));
            RaisePropertyChanged(nameof(EditorBottomBarHeight));
            RaisePropertyChanged(nameof(EditorModeLabel));
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public string EditorModeLabel => IsLiveMode ? "Back to edit" : "Live view";

    public bool IsPhotoPlaying => _photos.IsPhotoPlaying;
    public string PhotoPlaybackLabel => _photos.PhotoPlaybackLabel;
    public string PhotoPositionLabel => _photos.PhotoPositionLabel;

    public string? SelectedPort
    {
        get => _device.SelectedPort;
        set => _device.SelectedPort = value;
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
            NotifyThemeState();
        }
    }

    public string DeviceStatus
    {
        get => _device.DeviceStatus;
        private set => _device.DeviceStatus = value;
    }

    public DisplayProtocolProfile DisplayProtocol
    {
        get => _device.DisplayProtocol;
        set => _device.DisplayProtocol = value;
    }

    public DisplayColorMode DisplayColorMode
    {
        get => _device.DisplayColorMode;
        set => _device.DisplayColorMode = value;
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

    public bool RequestAdministratorAtStartup
    {
        get => _settings.RequestAdministratorAtStartup;
        set => _settings.RequestAdministratorAtStartup = value;
    }

    public bool LivePreview
    {
        get => _device.LivePreview;
        set => _device.LivePreview = value;
    }

    public bool UseLiveData => true;

    public bool IsDeviceBusy => _device.IsDeviceBusy;

    public bool CloseToTray
    {
        get => _settings.CloseToTray;
        set => _settings.CloseToTray = value;
    }

    public bool AutoStartDisplay
    {
        get => _settings.AutoStartDisplay;
        set => _settings.AutoStartDisplay = value;
    }

    public bool ShowAdvancedSensors
    {
        get => _settings.ShowAdvancedSensors;
        set
        {
            if (_settings.ShowAdvancedSensors == value)
                return;

            _settings.ShowAdvancedSensors = value;

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
        get => _settings.AdvancedDisplayExpanded;
        set => _settings.AdvancedDisplayExpanded = value;
    }

    public bool PositionPanelExpanded
    {
        get => _settings.PositionPanelExpanded;
        set => _settings.PositionPanelExpanded = value;
    }

    public bool DataPanelExpanded
    {
        get => _settings.DataPanelExpanded;
        set => _settings.DataPanelExpanded = value;
    }

    public bool TypographyPanelExpanded
    {
        get => _settings.TypographyPanelExpanded;
        set => _settings.TypographyPanelExpanded = value;
    }

    public bool GraphPanelExpanded
    {
        get => _settings.GraphPanelExpanded;
        set => _settings.GraphPanelExpanded = value;
    }

    public bool GaugePanelExpanded
    {
        get => _settings.GaugePanelExpanded;
        set => _settings.GaugePanelExpanded = value;
    }

    public bool MediaPanelExpanded
    {
        get => _settings.MediaPanelExpanded;
        set => _settings.MediaPanelExpanded = value;
    }

    public bool ShapePanelExpanded
    {
        get => _settings.ShapePanelExpanded;
        set => _settings.ShapePanelExpanded = value;
    }

    public bool ColoursPanelExpanded
    {
        get => _settings.ColoursPanelExpanded;
        set => _settings.ColoursPanelExpanded = value;
    }

    public string DisplayActionLabel => _device.IsConnected ? "Stop display" : "Start display";

    public double CanvasZoom
    {
        get => _settings.CanvasZoom;
        set => _settings.CanvasZoom = value;
    }

    public bool IsDirty => _themeSessions.IsDirty(Document.Mode);
    public bool HasUnsavedChanges => _themeSessions.HasUnsavedChanges || Workspace.IsDirty;
    public string WindowTitle => $"{CurrentThemeName}{(IsDirty ? " *" : string.Empty)} - PC Info Screen Studio";

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
    public RelayCommand ShowSetupAssistantCommand { get; }
    public RelayCommand ToggleEditorModeCommand { get; }

    public void SetEditorActive(bool active)
    {
        if (_isEditorActive == active) return;
        _isEditorActive = active;
        RaisePropertyChanged(nameof(IsEditorActive));
        RaisePropertyChanged(nameof(IsCanvasActive));

        if (active)
        {
            _dataTimer.Start();
            _animationTimer.Start();
            if (IsHardwarePage) _ = RefreshHardwarePageAsync();
            else _ = LoadThemeThumbnailsAsync();
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

    private void ToggleEditorMode()
    {
        IsLiveMode = !IsLiveMode;
        if (IsLiveMode)
        {
            ClearAlignmentGuides();
        }
    }

    public void SelectWidget(WidgetModel? widget, bool additive = false, bool toggle = false) => _editor.SelectWidget(widget, additive, toggle);

    public IReadOnlyList<WidgetModel> GetMovementTargets(WidgetModel anchor) => _editor.GetMovementTargets(anchor);

    public void SelectWidgets(IEnumerable<WidgetModel> widgets, bool additive) => _editor.SelectWidgets(widgets, additive);

    public (double X, double Y) ApplySmartAlignment(
        WidgetModel anchor,
        double x,
        double y,
        IReadOnlyCollection<WidgetModel> movingWidgets) => _editor.ApplySmartAlignment(anchor, x, y, movingWidgets);

    public void ClearAlignmentGuides() => _editor.ClearAlignmentGuides();

    public void NotifyDesignerChange()
    {
        MarkDirty();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SendLiveFrame(SkiaSharp.SKBitmap bitmap, bool force = false) => _device.SendLiveFrame(bitmap, force);

    private void AttachControllers()
    {
        _device.PropertyChanged += OnDevicePropertyChanged;
        _device.CommandStatesChanged += OnDeviceCommandStatesChanged;
        _device.LiveFrameRequested += OnLiveFrameRequested;
        _editor.PropertyChanged += OnEditorPropertyChanged;
        _editor.CommandStatesChanged += OnEditorCommandStatesChanged;
        _editor.AlignmentGuidesChanged += OnAlignmentGuidesChanged;
        _editor.DocumentChanged += OnEditorDocumentChanged;
        _photos.PropertyChanged += OnPhotoPlaybackPropertyChanged;
        _photos.CommandStatesChanged += OnPhotoCommandStatesChanged;
        _photos.DocumentChanged += OnPhotoDocumentChanged;
        _photos.PreviewChanged += OnPhotoPreviewChanged;
        _photos.LiveFrameRequested += OnPhotoLiveFrameRequested;
        _photos.StatusReported += OnPhotoStatusReported;
        _photos.SettingsReplacementRequested += ReplacePhotoFrameSettings;
        _settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    private void DetachControllers()
    {
        _device.PropertyChanged -= OnDevicePropertyChanged;
        _device.CommandStatesChanged -= OnDeviceCommandStatesChanged;
        _device.LiveFrameRequested -= OnLiveFrameRequested;
        _editor.PropertyChanged -= OnEditorPropertyChanged;
        _editor.CommandStatesChanged -= OnEditorCommandStatesChanged;
        _editor.AlignmentGuidesChanged -= OnAlignmentGuidesChanged;
        _editor.DocumentChanged -= OnEditorDocumentChanged;
        _photos.PropertyChanged -= OnPhotoPlaybackPropertyChanged;
        _photos.CommandStatesChanged -= OnPhotoCommandStatesChanged;
        _photos.DocumentChanged -= OnPhotoDocumentChanged;
        _photos.PreviewChanged -= OnPhotoPreviewChanged;
        _photos.LiveFrameRequested -= OnPhotoLiveFrameRequested;
        _photos.StatusReported -= OnPhotoStatusReported;
        _photos.SettingsReplacementRequested -= ReplacePhotoFrameSettings;
        _settings.PropertyChanged -= OnSettingsPropertyChanged;
    }

    private void OnDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
        => RaisePropertyChanged(e.PropertyName);

    private void OnDeviceCommandStatesChanged(object? sender, EventArgs e)
    {
        ConnectCommand?.RaiseCanExecuteChanged();
        DetectScreenCommand?.RaiseCanExecuteChanged();
        TestScreenCommand?.RaiseCanExecuteChanged();
        BenchmarkCommand?.RaiseCanExecuteChanged();
        FirstRunConnectCommand?.RaiseCanExecuteChanged();
    }

    private void OnLiveFrameRequested(object? sender, EventArgs e)
        => RequestLiveFrame?.Invoke(this, EventArgs.Empty);

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => RaisePropertyChanged(e.PropertyName);

    private void OnEditorCommandStatesChanged(object? sender, EventArgs e) => RaiseCommandStates();

    private void OnAlignmentGuidesChanged(object? sender, EventArgs e)
        => AlignmentGuidesChanged?.Invoke(this, EventArgs.Empty);

    private void OnEditorDocumentChanged(bool refresh)
    {
        if (refresh) MarkDirtyAndRefresh();
        else MarkDirty();
    }

    private void OnPhotoPlaybackPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => RaisePropertyChanged(e.PropertyName);

    private void OnPhotoCommandStatesChanged(object? sender, EventArgs e) => RaisePhotoCommandStates();

    private void OnPhotoDocumentChanged(object? sender, EventArgs e) => MarkDirtyAndRefresh();

    private void OnPhotoPreviewChanged(object? sender, EventArgs e)
        => ThemeChanged?.Invoke(this, EventArgs.Empty);

    private void OnPhotoLiveFrameRequested(object? sender, EventArgs e)
    {
        if (LivePreview) RequestLiveFrame?.Invoke(this, EventArgs.Empty);
    }

    private void OnPhotoStatusReported(string status) => DeviceStatus = status;

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Device compatibility properties are forwarded by DeviceController.
        if (e.PropertyName is not nameof(DisplayProtocol) and not nameof(DisplayColorMode)
            and not nameof(ShowAdvancedSensors))
            RaisePropertyChanged(e.PropertyName);
    }

    private void NewTheme()
    {
        if (Document.Mode != ScreenMode.InfoScreen) { NewModeTheme(); return; }
        if (!ConfirmDiscardIfNeeded()) return;
        ReplaceWorkspace(_packageService.CreateNewWorkspace());
        CreateStarterLayout();
    }

    private async Task OpenThemeAsync()
    {
        var path = Document.Mode == ScreenMode.InfoScreen ? _dialogs.OpenTheme() : _dialogs.OpenModeTheme(Document.Mode);
        if (path is null) return;
        await OpenThemeFileAsync(path);
    }

    public async Task OpenThemeFileAsync(string path)
    {
        if (Path.GetExtension(path).Equals(".pcphoto", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".pchybrid", StringComparison.OrdinalIgnoreCase))
        { LoadModeThemeFile(path); return; }
        if (Document.Mode != ScreenMode.InfoScreen)
        { MessageBox.Show("Switch to Info Screen to open a .t3theme file.", "Open theme"); return; }
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
        if (Document.Mode != ScreenMode.InfoScreen) { SaveModeThemeFile(saveAs); return; }
        var path = saveAs || string.IsNullOrWhiteSpace(Workspace.FilePath)
            ? _dialogs.SaveTheme(Document.Name)
            : Workspace.FilePath;
        if (path is null) return;

        try
        {
            await _packageService.SaveAsync(Workspace, path);
            SetActiveThemeLoaded(path);
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

    private void AddWidgetToCanvas(WidgetType type, string? dataSource) => _editor.AddWidgetToCanvas(type, dataSource);

    private WidgetModel NewWidget(WidgetType type) => _editor.NewWidget(type);

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

    private void AddPhotos() => _photos.AddPhotos();

    private void AddPhotoFolder() => _photos.AddPhotoFolder();

    public void AddPhotoFiles(IEnumerable<string> files) => _photos.AddPhotoFiles(files);

    private void RemoveSelectedPhoto() => _photos.RemoveSelectedPhoto();

    public void MovePhoto(PhotoFrameItem source, PhotoFrameItem target) => _photos.MovePhoto(source, target);

    public void PreviousPhoto() => _photos.PreviousPhoto();

    public void NextPhoto() => _photos.NextPhoto();

    public void TogglePhotoPlayback() => _photos.TogglePhotoPlayback();

    private void SaveAlbumPreset() => _photos.SaveAlbumPreset();

    private void LoadAlbumPreset() => _photos.LoadAlbumPreset();

    private void ChooseWatchedFolder() => _photos.ChooseWatchedFolder();

    private static bool IsSupportedPhoto(string path) => PhotoPlaybackController.IsSupportedPhoto(path);

    private void InitializePhotoFrameRuntime() => _photos.InitializePhotoFrameRuntime();

    private bool UpdatePhotoPlayback() => _photos.UpdatePhotoPlayback();

    private void SetCurrentPhoto(int index, bool manual) => _photos.SetCurrentPhoto(index, manual);

    private bool UpdateEffectiveScreenMode(bool force = false) => _photos.UpdateEffectiveScreenMode(force);

    private void ConfigurePhotoFolderWatcher() => _photos.ConfigurePhotoFolderWatcher();

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

    private void DeleteSelected() => _editor.DeleteSelected();

    private void DuplicateSelected() => _editor.DuplicateSelected();

    private void NudgeSelected(object? parameter) => _editor.NudgeSelected(parameter);

    private void AlignSelected(object? parameter) => _editor.AlignSelected(parameter);

    private void GroupSelected() => _editor.GroupSelected();

    private void UngroupSelected() => _editor.UngroupSelected();

    private void MoveLayer(int delta) => _editor.MoveLayer(delta);

    private void NormalizeZIndices() => _editor.NormalizeZIndices();

    private void ToggleOrientation() => _editor.ToggleOrientation();

    private void RotateDevice()
    {
        Document.DeviceRotation = Document.DeviceRotation switch
        {
            DeviceRotation.Degrees0 => DeviceRotation.Degrees90,
            DeviceRotation.Degrees90 => DeviceRotation.Degrees180,
            DeviceRotation.Degrees180 => DeviceRotation.Degrees270,
            _ => DeviceRotation.Degrees0
        };
        if (_device.IsConnected)
            _ = ApplyDeviceOrientationAsync();

        MarkDirtyAndRefresh();
    }

    private void RefreshPorts() => _device.RefreshPorts();

    private void DetectScreen() => _device.DetectScreen();

    private async Task DetectAndConnectFirstRunAsync()
    {
        if (_device.IsConnected)
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
        FirstRunStatus = _device.IsConnected
            ? "Screen connected. Choose a mode and finish setup."
            : "The screen could not be connected. You can finish setup and retry from Device Settings.";
    }

    private void CompleteFirstRun()
    {
        if (FirstRunUseStarterTheme)
            CreateStarterLayout();
        else
            CreateBlankLayout();

        _settings.FirstRunCompleted = true;
        IsFirstRunVisible = false;
    }

    private void ShowSetupAssistant()
    {
        FirstRunStatus = _device.IsConnected
            ? "Screen is connected. Review the mode and starting layout."
            : "Connect your screen now, or finish setup and connect later.";
        IsFirstRunVisible = true;
    }

    private Task ConnectOrDisconnectAsync() => _device.ConnectOrDisconnectAsync();

    public void StartAutoDisplayIfEnabled()
    {
        if (!AutoStartDisplay || _device.IsConnected || IsDeviceBusy)
            return;

        DetectScreen();
        if (!string.IsNullOrWhiteSpace(SelectedPort))
            _ = ConnectOrDisconnectAsync();
    }

    private Task TestScreenAsync() => _device.TestScreenAsync();

    private Task ApplyDisplayCompatibilityAsync() => _device.ApplyDisplayCompatibilityAsync();

    private Task ApplyDeviceOrientationAsync() => _device.ApplyDeviceOrientationAsync();

    private Task RunBenchmarkAsync() => _device.RunBenchmarkAsync();

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
        var previousPath = ActiveThemePath ?? SelectedModeTheme?.FilePath;
        ModeThemes.Clear();
        foreach (var theme in _modeThemeService.GetThemes(Document.Mode))
            ModeThemes.Add(theme);
        if (Document.Mode != ScreenMode.InfoScreen && ActiveThemePath is string activePath && File.Exists(activePath) && !ModeThemes.Any(t => t.FilePath.Equals(activePath, StringComparison.OrdinalIgnoreCase)))
            ModeThemes.Add(new ModeThemeLibraryItem(Path.GetFileNameWithoutExtension(activePath), activePath));
        SelectedModeTheme = ModeThemes.FirstOrDefault(theme =>
                                string.Equals(theme.FilePath, previousPath, StringComparison.OrdinalIgnoreCase))
                            ?? ModeThemes.FirstOrDefault();
    }

    private void SaveModeTheme() => SaveModeThemeFile(false);

    private void LoadModeTheme()
    {
        if (SelectedModeTheme is { } theme) LoadModeThemeFile(theme.FilePath);
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
        try
        {
            var path = SelectedModeTheme.FilePath;
            _modeThemeService.Delete(path);
            _themeSessions.MovePath(path, null);
            Workspace.IsDirty = _themeSessions.HasUnsavedChanges;
            NotifyThemeState();
            RefreshModeThemes();
        }
        catch (Exception ex)
        { MessageBox.Show(ex.Message, "Could not delete settings theme", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ApplyGlobalPhotoSettings(PhotoFrameSettings source)
        => ModeThemeService.ApplySettings(Document.PhotoFrame, source);

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
        if (Document.Mode != ScreenMode.InfoScreen) { ManageModeTheme(false); return; }
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
        if (Document.Mode != ScreenMode.InfoScreen) { ManageModeTheme(true); return; }
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
        if (Document.Mode != ScreenMode.InfoScreen) { DeleteModeTheme(); return; }
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
        if (Document.Mode != ScreenMode.InfoScreen) { LoadModeTheme(); return; }
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
        _themeSessions.Reset();
        _lastThemeMode = Document.Mode;
        _themeSessions.Loaded(Document.Mode, workspace.FilePath);
        if (workspace.IsDirty) _themeSessions.MarkDirty(Document.Mode);
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
        RefreshModeThemes();
        NotifyThemeState();
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
        if (e.PropertyName == nameof(ThemeDocument.EditorWidgets)) return;
        if (e.PropertyName == nameof(ThemeDocument.Mode))
        {
            var wasSuppressed = _suppressDirty;
            _suppressDirty = true;
            try
            {
                var previousFolder = Document.PhotoFrame.WatchedFolder;
                _themeSessions.SwitchSettings(_lastThemeMode, Document.Mode, Document.PhotoFrame);
                if (Document.Mode != ScreenMode.InfoScreen &&
                    !string.Equals(previousFolder, Document.PhotoFrame.WatchedFolder, StringComparison.OrdinalIgnoreCase))
                {
                    var warning = _photos.RestoreThemeFolder();
                    if (warning is not null) DeviceStatus = warning;
                }
            }
            finally { _suppressDirty = wasSuppressed; }
            _lastThemeMode = Document.Mode;
            UpdateEffectiveScreenMode(force: true);
            SelectWidget(Document.EditorWidgets.OrderBy(widget => widget.ZIndex).FirstOrDefault());
            RefreshModeThemes();
            RaisePropertyChanged(nameof(ModeThemeTitle));
            RaisePropertyChanged(nameof(ModeThemeSummary));
            RaisePropertyChanged(nameof(PropertiesPanelWidth));
            RaisePropertyChanged(nameof(LeftPanelWidth));
            SaveModeThemeCommand.RaiseCanExecuteChanged();
            NotifyThemeState();
            ThemeChanged?.Invoke(this, EventArgs.Empty);
            if (LivePreview) RequestLiveFrame?.Invoke(this, EventArgs.Empty);
            return;
        }
        MarkDirtyAndRefresh();

        if (_device.IsConnected &&
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
        if (e.PropertyName is nameof(WidgetModel.IsSelected) or nameof(WidgetModel.RuntimeValue) or nameof(WidgetModel.RuntimeText) or nameof(WidgetModel.RuntimeUnit) or nameof(WidgetModel.BackgroundTransparency)) return;
        if (e.PropertyName == nameof(WidgetModel.DataSource) && sender is WidgetModel widget)
            ApplyDataSourceDefaults(widget);
        MarkDirtyAndRefresh();
    }

    private static void ApplyDataSourceDefaults(WidgetModel widget) => EditorController.ApplyDataSourceDefaults(widget);

    private static string FriendlyLabel(string source) => EditorController.FriendlyLabel(source);

    private static bool IsTemperatureSource(string source) => EditorController.IsTemperatureSource(source);

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
        if (_isDisposed) return;

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

            if (IsHardwarePageVisible) HardwareDashboard.Update(sample);
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

            if (IsCanvasActive)
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
        _settings.WeatherCity = WeatherCity;

        if (string.IsNullOrWhiteSpace(WeatherCity))
        {
            WeatherStatus = "Weather city not configured.";
            await RefreshRuntimeDataAsync(forceWeather: true);
            return;
        }

        WeatherStatus = $"Looking up {WeatherCity}...";

        await RefreshRuntimeDataAsync(forceWeather: true);
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
            "\n\nLive sensor readings are enabled for your widgets.",
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
        _themeSessions.MarkDirty(Document.Mode);
        Workspace.IsDirty = true;
        NotifyThemeState();
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
            _lastThemeMode = Document.Mode;
            FontAssets.Clear();
            foreach (var font in Document.Assets.Where(a => a.Kind == ThemeAssetKind.Font))
                FontAssets.Add(font);

            InitializePhotoFrameRuntime();
            SelectedWidget = Document.EditorWidgets.FirstOrDefault(w => w.Id == selectedId)
                             ?? Document.EditorWidgets.FirstOrDefault();
            _themeSessions.MarkDirty(Document.Mode);
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
        if (Document.Mode == ScreenMode.InfoScreen ? !HasUnsavedChanges : !IsDirty) return true;
        var result = MessageBox.Show("This theme has unsaved changes. Continue and discard them?", "Unsaved changes", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }

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
        _isDisposed = true;
        _dataTimer.Stop();
        _animationTimer.Stop();
        _historyTimer.Stop();
        _recoveryTimer.Stop();
        DeleteRecoveryFile();
        _weatherMetrics.Dispose();
        _hardwareMetrics.Dispose();
        DetachControllers();
        _photos.Dispose();
        ThemeRenderer.ClearCaches();
        _device.Dispose();
        DetachWorkspace(_workspace);
        _workspace.Dispose();
    }
}
