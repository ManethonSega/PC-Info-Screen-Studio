using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
    private readonly DispatcherTimer _dataTimer;
    private ThemeWorkspace _workspace;
    private WidgetModel? _selectedWidget;
    private string? _selectedPort;
    private ThemeLibraryItem? _selectedTheme;
    private string _deviceStatus = "Not connected";
    private bool _livePreview;
    private bool _useLiveData;
    private bool _suppressDirty;
    private bool _isDeviceBusy;
    private int _frameSendBusy;

    public MainViewModel()
    {
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
        BenchmarkCommand = new RelayCommand(() => _ = RunBenchmarkAsync(), () => _deviceService.IsConnected && !IsDeviceBusy);
        RefreshThemesCommand = new RelayCommand(RefreshThemes);
        LoadThemeCommand = new RelayCommand(() => _ = LoadSelectedThemeAsync(), () => SelectedTheme is not null);

        _dataTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _dataTimer.Tick += (_, _) => RefreshRuntimeData();
        _dataTimer.Start();

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

    public IReadOnlyList<string> DataSources { get; } =
    [
        "Preview.Value",
        "CPU.Usage", "CPU.Temperature", "CPU.Power", "CPU.Clock",
        "GPU.Usage", "GPU.Temperature", "GPU.Hotspot", "GPU.VRAM", "GPU.Power", "GPU.FanRPM",
        "RAM.Usage", "RAM.UsedGB", "RAM.AvailableGB", "RAM.TotalGB",
        "Disk.Usage", "Disk.FreeGB", "Disk.Temperature", "Disk.Read", "Disk.Write",
        "Network.Download", "Network.Upload",
        "Cooling.FanRPM", "Cooling.PumpRPM",
        "Weather.Temperature", "Weather.FeelsLike", "Weather.Humidity", "Weather.Wind",
        "Clock.Time", "Clock.Date", "Clock.Day"
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
                RefreshRuntimeData();
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
    public RelayCommand BenchmarkCommand { get; }
    public RelayCommand RefreshThemesCommand { get; }
    public RelayCommand LoadThemeCommand { get; }

    public void SelectWidget(WidgetModel? widget) => SelectedWidget = widget;

    public void NotifyDesignerChange()
    {
        MarkDirty();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SendLiveFrame(SkiaSharp.SKBitmap bitmap)
    {
        if (!LivePreview || !_deviceService.IsConnected) return;

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
                DeviceStatus = $"Connected: {_deviceService.ConnectedPort} @ {_deviceService.ConnectedBaudRate ?? 0} baud");
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
            WidgetType.CircularGauge => new WidgetModel { Type = type, Name = "Circular gauge", Label = "CPU", DataSource = "CPU.Usage", Width = 110, Height = 110, X = centerX, Y = centerY, FontSize = 26 },
            WidgetType.BarGauge => new WidgetModel { Type = type, Name = "Bar gauge", Label = "RAM", DataSource = "RAM.Usage", Width = 180, Height = 62, X = centerX, Y = centerY, FontSize = 22, SegmentCount = 18 },
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
            await _deviceService.ConnectAsync(SelectedPort, Document.Orientation, Document.DeviceRotation);
            DeviceStatus = $"Connected: {SelectedPort} @ {_deviceService.ConnectedBaudRate ?? 0} baud";
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
        if (e.PropertyName is nameof(WidgetModel.IsSelected) or nameof(WidgetModel.RuntimeValue) or nameof(WidgetModel.RuntimeText)) return;
        if (e.PropertyName == nameof(WidgetModel.DataSource) && sender is WidgetModel widget)
            ApplyDataSourceDefaults(widget);
        MarkDirtyAndRefresh();
    }

    private static void ApplyDataSourceDefaults(WidgetModel widget)
    {
        var source = widget.DataSource ?? string.Empty;
        if (source.StartsWith("Clock.", StringComparison.OrdinalIgnoreCase))
        {
            widget.Suffix = string.Empty;
            return;
        }

        if (source.Contains("Temperature", StringComparison.OrdinalIgnoreCase) || source.Contains("Hotspot", StringComparison.OrdinalIgnoreCase) || source.StartsWith("Weather.Temperature", StringComparison.OrdinalIgnoreCase) || source.StartsWith("Weather.FeelsLike", StringComparison.OrdinalIgnoreCase))
        {
            widget.Suffix = "°C"; widget.Minimum = 0; widget.Maximum = 100; return;
        }
        if (source.EndsWith("Usage", StringComparison.OrdinalIgnoreCase) || source.EndsWith("VRAM", StringComparison.OrdinalIgnoreCase))
        {
            widget.Suffix = "%"; widget.Minimum = 0; widget.Maximum = 100; return;
        }
        if (source.EndsWith("GB", StringComparison.OrdinalIgnoreCase))
        {
            widget.Suffix = " GB"; widget.Minimum = 0; widget.Maximum = Math.Max(64, widget.Maximum); return;
        }
        if (source.Contains("Network.", StringComparison.OrdinalIgnoreCase) || source.EndsWith("Read", StringComparison.OrdinalIgnoreCase) || source.EndsWith("Write", StringComparison.OrdinalIgnoreCase))
        {
            widget.Suffix = " MB/s"; widget.Minimum = 0; widget.Maximum = Math.Max(100, widget.Maximum); return;
        }
        if (source.Contains("FanRPM", StringComparison.OrdinalIgnoreCase) || source.Contains("PumpRPM", StringComparison.OrdinalIgnoreCase))
        {
            widget.Suffix = " RPM"; widget.Minimum = 0; widget.Maximum = Math.Max(5000, widget.Maximum); return;
        }
        if (source.EndsWith("Power", StringComparison.OrdinalIgnoreCase))
        {
            widget.Suffix = " W"; widget.Minimum = 0; widget.Maximum = Math.Max(400, widget.Maximum); return;
        }
        if (source.EndsWith("Clock", StringComparison.OrdinalIgnoreCase))
        {
            widget.Suffix = " MHz"; widget.Minimum = 0; widget.Maximum = Math.Max(6000, widget.Maximum);
        }
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

    private void RefreshRuntimeData()
    {
        if (!UseLiveData) return;

        var sample = _systemMetrics.Sample();
        foreach (var widget in Document.Widgets)
        {
            if (sample.TryGetValue(widget.DataSource, out var value))
            {
                widget.RuntimeValue = value.Numeric;
                widget.RuntimeText = value.Text;
                if (widget.Type == WidgetType.Text && value.Numeric is double numeric)
                    widget.RuntimeText = numeric.ToString(widget.ValueFormat) + (value.Unit ?? string.Empty);

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
                    widget.RuntimeValue = 0;
                    widget.RuntimeText = "N/A";
                }
                else
                {
                    widget.RuntimeValue = null;
                    widget.RuntimeText = null;
                }
            }
        }

        ThemeChanged?.Invoke(this, EventArgs.Empty);
        if (LivePreview) RequestLiveFrame?.Invoke(this, EventArgs.Empty);
    }

    private void ClearRuntimeData()
    {
        foreach (var widget in Document.Widgets)
        {
            widget.RuntimeValue = null;
            widget.RuntimeText = null;
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
        _deviceService.Dispose();
        DetachWorkspace(_workspace);
        _workspace.Dispose();
    }
}
