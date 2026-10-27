using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;
using PCInfoScreenStudio.Services.Matrix;
using PCInfoScreenStudio.Rendering;
using SkiaSharp;

namespace PCInfoScreenStudio.ViewModels;

public sealed class MatrixWorkspaceViewModel : ObservableObject, IDisposable
{
    private MatrixSettings _draft;
    private MatrixSettings? _applied;
    private readonly Matrix900Device _device;
    private readonly Action<MatrixSettings> _save;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private IReadOnlyDictionary<string, MetricValue> _sample = new Dictionary<string, MetricValue>();
    private Guid? _selectedWidget;
    private readonly MatrixWidgetSettings _empty = new() { Name = "", Text = "" };
    private bool _painting;
    private readonly DispatcherTimer _outputTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private long _scrollStarted, _lastHeartbeat;
    private bool _wasVisible;
    private bool _busy, _heartbeatRunning, _disposed;
    private DateTime _sampleAt;
    private string _status = "Preview ready. Start display to connect the Matrix 900.";
    public event EventHandler? PreviewChanged;
    public event EventHandler? ConnectionChanged;
    public MatrixWorkspaceViewModel(MatrixSettings settings, Action<MatrixSettings> save, Matrix900Device? device = null)
    {
        _draft = settings.ToWidgetWorkspace(); _selectedWidget = _draft.Widgets!.FirstOrDefault()?.Id; _save = save; _device = device ?? new Matrix900Device();
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); _save(Snapshot); };
        _outputTimer.Tick += async (_, _) => await TickAsync();
        ApplyCommand = new RelayCommand(() => _ = ApplyAsync(), () => IsConnected && !IsBusy && Validation.Length == 0);
        ImportImageCommand = new RelayCommand(ImportImage, () => !IsBusy && IsEditable);
        AddTextCommand = new RelayCommand(() => AddWidget(MatrixContent.Text), () => Layers.Count < 64);
        AddValueCommand = new RelayCommand(() => AddWidget(MatrixContent.Sensor), () => Layers.Count < 64);
        AddClockCommand = new RelayCommand(() => AddWidget(MatrixContent.Clock), () => Layers.Count < 64);
        AddPixelArtCommand = new RelayCommand(() => AddWidget(MatrixContent.PixelArt), () => Layers.Count < 64);
        AddEmojiCommand = new RelayCommand(() => AddWidget(MatrixContent.Text, true), () => Layers.Count < 64);
        DeleteCommand = new RelayCommand(DeleteWidget, () => IsEditable);
        DuplicateCommand = new RelayCommand(DuplicateWidget, () => HasSelection && Layers.Count < 64);
        LayerUpCommand = new RelayCommand(() => Reorder(1), () => IsEditable && WidgetIndex < _draft.Widgets!.Count - 1);
        LayerDownCommand = new RelayCommand(() => Reorder(-1), () => IsEditable && WidgetIndex > 0);
        ClearPixelsCommand = new RelayCommand(() => { if (!IsEditable) return; Tile.Pixels = new byte[2700]; Changed(); }, () => IsEditable);
    }
    public int Layout => 1;
    public IReadOnlyList<MatrixSourceOption> Sources => MatrixTileSettings.Sources;
    public IReadOnlyList<MatrixWidgetSizeOption> Sizes { get; } = [new("10 dots", 10), new("5 dots", 5), new("4 dots", 4)];
    public IReadOnlyList<MatrixWidgetSettings> Layers => _draft.Widgets!.AsEnumerable().Reverse().ToArray();
    public Guid? SelectedWidgetId
    {
        get => _selectedWidget;
        set
        {
            if ((!value.HasValue && _draft.Widgets!.Count != 0) || value == _selectedWidget || (value.HasValue && !_draft.Widgets!.Any(w => w.Id == value))) return;
            _selectedWidget = value; _painting = false; RefreshFields(); PreviewChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public MatrixWidgetSettings? SelectedWidget => _draft.Widgets!.FirstOrDefault(w => w.Id == SelectedWidgetId);
    public MatrixWidgetSettings Tile => SelectedWidget ?? _empty;
    private int WidgetIndex => SelectedWidget is { } w ? _draft.Widgets!.IndexOf(w) : -1;
    public bool HasSelection => SelectedWidget is not null;
    public bool IsEditable => HasSelection && !Tile.Locked;
    public Visibility SelectionVisibility => HasSelection ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyVisibility => HasSelection ? Visibility.Collapsed : Visibility.Visible;
    public string SelectedTileLabel => HasSelection ? Tile.Name : "Select or add a widget";
    public (int X, int Y, int Width, int Height) SelectedContentRegion => (Tile.X, Tile.Y, Tile.Width, Tile.Height);
    public string Name { get => Tile.Name; set { if (!IsEditable) return; Tile.Name = value ?? ""; RaisePropertyChanged(); RaisePropertyChanged(nameof(Layers)); RaisePropertyChanged(nameof(SelectedWidgetId)); RaisePropertyChanged(nameof(SelectedTileLabel)); Changed(); } }
    public int X { get => Tile.X; set => MoveSelectedTo(value, Y); }
    public int Y { get => Tile.Y; set => MoveSelectedTo(X, value); }
    public int Width { get => Tile.Width; set { if (!IsEditable) return; Tile.Width = Math.Clamp(value, 1, 90); Tile.X = Math.Min(Tile.X, 90 - Tile.Width); RefreshFields(); Changed(); } }
    public int WidgetHeight { get => Tile.Height; set { if (!IsEditable || value is not (4 or 5 or 10) || value == Tile.Height) return; Tile.Height = value; Tile.Y = Math.Min(Tile.Y, 10 - value); RefreshFields(); Changed(); } }
    public bool IsPainting { get => _painting; set { _painting = value && IsEditable && ContentKind == MatrixContent.PixelArt; RaisePropertyChanged(); PreviewChanged?.Invoke(this, EventArgs.Empty); } }
    public IReadOnlyList<MatrixFourDotFontOption> FourDotFonts { get; } =
        [new("mem 4×4", MatrixFourDotFont.Mem), new("mem mono 4×4", MatrixFourDotFont.MemMono), new("Able 4", MatrixFourDotFont.Able4), new("Limitdot H4", MatrixFourDotFont.LimitdotH4)];
    public IReadOnlyList<MatrixFontOption> FiveDotFonts { get; } =
        [new("Tom Thumb", MatrixFiveDotFont.TomThumb), new("PixelFont 5×5 Thin", MatrixFiveDotFont.Pixel5x5Thin),
         new("Petty 5H", MatrixFiveDotFont.Petty5H), new("Petty 5×5", MatrixFiveDotFont.Petty5x5),
         new("Able 5", MatrixFiveDotFont.Able5), new("mem 5×6", MatrixFiveDotFont.Mem5x6)];
    public IReadOnlyList<MatrixTenDotFontOption> TenDotFonts { get; } =
        [new("PixelFont 5×5 Thin ×2", MatrixTenDotFont.Pixel5x5Double), new("PixelFont 5×10 Condensed", MatrixTenDotFont.Pixel5x10Condensed),
         new("Micro 3×10", MatrixTenDotFont.Micro3x10), new("piconano 3×10", MatrixTenDotFont.Piconano3x10)];
    public IReadOnlyList<MatrixAlignmentOption> Alignments { get; } =
        [new("Left", MatrixAlignment.Left), new("Centre", MatrixAlignment.Centre), new("Right", MatrixAlignment.Right)];
    public MatrixAlignment Alignment { get => Tile.Alignment; set { if (!IsEditable) return; if (!Enum.IsDefined(value) || value == Alignment) return; Tile.Alignment = value; RaisePropertyChanged(); Changed(); } }
    public Visibility AlignmentVisibility => ContentKind != MatrixContent.PixelArt ? Visibility.Visible : Visibility.Collapsed;
    public MatrixFourDotFont FourDotFont { get => Tile.FourDotFont ?? MatrixFourDotFont.Mem; set { if (!IsEditable) return; if (!Enum.IsDefined(value) || value == FourDotFont) return; Tile.FourDotFont = value; RefreshFields(); Changed(); } }
    public MatrixFiveDotFont FiveDotFont { get => Tile.FiveDotFont ?? MatrixFiveDotFont.TomThumb; set { if (!IsEditable) return; if (!Enum.IsDefined(value) || value == FiveDotFont) return; Tile.FiveDotFont = value; RefreshFields(); Changed(); } }
    public MatrixTenDotFont TenDotFont { get => Tile.TenDotFont ?? MatrixTenDotFont.Pixel5x5Double; set { if (!IsEditable) return; if (!Enum.IsDefined(value) || value == TenDotFont) return; Tile.TenDotFont = value; RefreshFields(); Changed(); } }
    public Visibility FourDotFontVisibility => FontVisibility(4);
    public Visibility FiveDotFontVisibility => FontVisibility(5);
    public Visibility TenDotFontVisibility => FontVisibility(10);
    private Visibility FontVisibility(int height) => ContentKind != MatrixContent.PixelArt && SelectedContentRegion.Height == height ? Visibility.Visible : Visibility.Collapsed;
    public string FontNote => ContentKind == MatrixContent.PixelArt ? ""
        : SelectedContentRegion.Height == 5 && FiveDotFont == MatrixFiveDotFont.Mem5x6 ? "Five-dot capitals; Q is fitted within five rows."
        : SelectedContentRegion.Height == 5 && FiveDotFont is MatrixFiveDotFont.Petty5H or MatrixFiveDotFont.Petty5x5 ? "Uppercase font. Lowercase text uses capitals."
        : SelectedContentRegion.Height == 10 && TenDotFont != MatrixTenDotFont.Pixel5x5Double ? "Tall adaptation: height doubled, width unchanged." : "";
    public MatrixSettings Snapshot => _draft.ValidatedCopy();
    public MatrixContent ContentKind { get => Tile.Content; set { if (!IsEditable) return; if (!Enum.IsDefined(value) || value == Tile.Content) return; Tile.Content = value; Changed(); RefreshFields(); } }
    public string Text { get => Tile.Text; set { if (!IsEditable) return; Tile.Text = value ?? ""; RaisePropertyChanged(); Changed(); } }
    public string Source { get => Tile.Source; set { if (!IsEditable) return; if (!Sources.Any(s => s.Key == value)) return; Tile.Source = value; RaisePropertyChanged(); Changed(); } }
    public string Colour { get => Tile.Colour; set { if (!IsEditable) return; if (value?.Length == 9) value = "#" + value[3..]; if (!System.Text.RegularExpressions.Regex.IsMatch(value ?? "", "^#[0-9a-fA-F]{6}$")) return; Tile.Colour = value!; RaisePropertyChanged(); Changed(); } }
    public bool Scroll { get => Tile.Scroll; set { if (!IsEditable) return; Tile.Scroll = value; RaisePropertyChanged(); Changed(); } }
    public bool Alarm { get => Tile.Alarm; set { if (!IsEditable) return; Tile.Alarm = value; RaisePropertyChanged(); Changed(); } }
    public int AlarmThreshold { get => Tile.AlarmThreshold; set { if (!IsEditable) return; Tile.AlarmThreshold = Math.Clamp(value, 0, 100); RaisePropertyChanged(); Changed(); } }
    public int Brightness { get => _draft.Brightness; set { _draft.Brightness = Math.Clamp(value, 0, 100); if (_applied is not null) _applied.Brightness = _draft.Brightness; RaisePropertyChanged(); Changed(); ConnectionChanged?.Invoke(this, EventArgs.Empty); } }
    public bool IsConnected => _device.IsConnected;
    public string? FirmwareVersion => _device.FirmwareVersion;
    public bool IsBusy { get => _busy; private set { _busy = value; RaisePropertyChanged(); ApplyCommand.RaiseCanExecuteChanged(); ImportImageCommand.RaiseCanExecuteChanged(); RaisePropertyChanged(nameof(FontNote)); ConnectionChanged?.Invoke(this, EventArgs.Empty); } }
    public string Status { get => _status; private set { _status = value; RaisePropertyChanged(); } }
    public string Validation
    {
        get
        {
            for (int i = 0; i < _draft.Widgets!.Count; i++)
            {
                var tile = _draft.Widgets[i];
                if (!tile.Visible) continue;
                if (tile.Content == MatrixContent.Text && (System.Globalization.StringInfo.ParseCombiningCharacters(tile.Text).Length > 50 || tile.Text.Any(char.IsControl)))
                    return "Use one line of up to 50 characters or emoji per widget.";

            }
            return "";
        }
    }
    public Visibility TextVisibility => ContentKind == MatrixContent.Text ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SensorVisibility => ContentKind == MatrixContent.Sensor ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PixelVisibility => ContentKind == MatrixContent.PixelArt ? Visibility.Visible : Visibility.Collapsed;
    public string PreviewReading => Tile.Content == MatrixContent.Sensor ? Reading(Tile.Source) : Tile.Content == MatrixContent.Clock ? DateTime.UtcNow.ToString("HH:mm") : Tile.Text;
    public RelayCommand AddTextCommand { get; }
    public RelayCommand AddValueCommand { get; }
    public RelayCommand AddClockCommand { get; }
    public RelayCommand AddPixelArtCommand { get; }
    public RelayCommand AddEmojiCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand LayerUpCommand { get; }
    public RelayCommand LayerDownCommand { get; }
    public RelayCommand ApplyCommand { get; }
    public RelayCommand ImportImageCommand { get; }
    public RelayCommand ClearPixelsCommand { get; }
    public void Initialize() => RefreshFields();
    public void AddWidget(MatrixContent content, bool emoji = false)
    {
        if (_draft.Widgets!.Count >= 64) return;
        var widget = new MatrixWidgetSettings { Content = content, Name = emoji ? "Emoji" : content == MatrixContent.Sensor ? "Value" : content == MatrixContent.Clock ? "Clock" : content == MatrixContent.PixelArt ? "Pixel art" : "Text", Text = emoji ? "👌" : "Text", Height = 5, Width = emoji ? 5 : content == MatrixContent.PixelArt ? 10 : 30, Scroll = false };
        _draft.Widgets.Add(widget.CopyWidget(_draft)); SelectedWidgetId = _draft.Widgets[^1].Id; Changed();
    }
    public void MoveSelectedTo(int x, int y)
    {
        if (!IsEditable) return;
        x = Math.Clamp(x, 0, 90 - Tile.Width); y = Math.Clamp(y, 0, 10 - Tile.Height);
        if (x == Tile.X && y == Tile.Y) return;
        Tile.X = x; Tile.Y = y; RaisePropertyChanged(nameof(X)); RaisePropertyChanged(nameof(Y)); Changed();
    }
    public void LayerFlagsChanged() { if (!IsEditable) IsPainting = false; RaisePropertyChanged(nameof(IsEditable)); RefreshCommands(); Changed(); }
    private void DeleteWidget()
    {
        if (!IsEditable) return;
        int index = WidgetIndex; _draft.Widgets!.RemoveAt(index);
        _selectedWidget = _draft.Widgets.Count == 0 ? null : _draft.Widgets[Math.Min(index, _draft.Widgets.Count - 1)].Id;
        _painting = false; RefreshFields(); Changed();
    }
    private void DuplicateWidget()
    {
        if (!HasSelection || _draft.Widgets!.Count >= 64) return;
        var copy = Tile.CopyWidget(_draft); copy.Id = Guid.NewGuid(); copy.Name += " copy"; copy.Locked = false;
        copy.X = Math.Min(copy.X + 1, 90 - copy.Width); copy.Y = Math.Min(copy.Y + 1, 10 - copy.Height);
        _draft.Widgets.Insert(WidgetIndex + 1, copy); SelectedWidgetId = copy.Id; Changed();
    }
    private void Reorder(int direction)
    {
        if (!IsEditable) return;
        int index = WidgetIndex, next = index + direction;
        if (next < 0 || next >= _draft.Widgets!.Count) return;
        var item = Tile; _draft.Widgets.RemoveAt(index); _draft.Widgets.Insert(next, item); RefreshFields(); Changed();
    }
    public MatrixWidgetSettings? HitWidget(int x, int y) => _draft.Widgets!.LastOrDefault(w => w.Visible && x >= w.X && x < w.X + w.Width && y >= w.Y && y < w.Y + w.Height);

    private void RefreshFields()
    {
        foreach (var name in new[] { nameof(Layers), nameof(SelectedWidgetId), nameof(HasSelection), nameof(IsEditable), nameof(SelectionVisibility), nameof(EmptyVisibility), nameof(Name), nameof(X), nameof(Y), nameof(Width), nameof(WidgetHeight), nameof(IsPainting), nameof(FourDotFont), nameof(FiveDotFont), nameof(TenDotFont), nameof(FourDotFontVisibility), nameof(FiveDotFontVisibility), nameof(TenDotFontVisibility), nameof(FontNote), nameof(Alignment), nameof(AlignmentVisibility), nameof(SelectedTileLabel), nameof(ContentKind), nameof(Text), nameof(Source), nameof(Colour), nameof(Scroll), nameof(Alarm), nameof(AlarmThreshold), nameof(Brightness), nameof(TextVisibility), nameof(SensorVisibility), nameof(PixelVisibility), nameof(PreviewReading), nameof(Validation) }) RaisePropertyChanged(name);
        RefreshCommands();
    }
    private void RefreshCommands()
    {
        foreach (var command in new[] { AddTextCommand, AddValueCommand, AddClockCommand, AddPixelArtCommand, AddEmojiCommand, DeleteCommand, DuplicateCommand, LayerUpCommand, LayerDownCommand, ClearPixelsCommand, ImportImageCommand }) command.RaiseCanExecuteChanged();
    }
    private void Changed()
    {
        if (_disposed) return;
        _saveTimer.Stop(); _saveTimer.Start(); RaisePropertyChanged(nameof(Validation)); RaisePropertyChanged(nameof(PreviewReading));
        ApplyCommand.RaiseCanExecuteChanged(); PreviewChanged?.Invoke(this, EventArgs.Empty);
    }
    public async Task ToggleAsync()
    {
        if (IsBusy || _disposed) return;
        if (IsConnected) { await StopAsync(); return; }
        IsBusy = true;
        try
        {
            await _device.ConnectAsync(_lifetime.Token);
            Status = "Matrix HID connected. Apply the layout, then verify the physical panel.";
        }
        catch (Exception ex) { Status = "Could not start Matrix: " + ex.Message; }
        finally { IsBusy = false; ConnectionChanged?.Invoke(this, EventArgs.Empty); }
    }
    public async Task ApplyAsync()
    {
        if (!IsConnected || IsBusy || _disposed || Validation.Length != 0) return;
        var snapshot = Snapshot;
        for (int i = 0; i < snapshot.Widgets!.Count; i++)
            if (snapshot.Widgets[i].Visible && snapshot.Widgets[i].Content == MatrixContent.Sensor && !TryValue(snapshot.Widgets[i].Source, out _))
            { Status = "A selected sensor has no current reading. Choose an available source or enable full sensors before applying."; return; }
        _outputTimer.Stop(); _applied = null;
        IsBusy = true; Status = "Sending layout to the Matrix...";
        try
        {
            await _device.ConfigureAsync(RenderOutput(snapshot, DateTime.UtcNow, 0), _lifetime.Token); _applied = snapshot;
            _scrollStarted = Environment.TickCount64; _lastHeartbeat = 0; _wasVisible = true;
            if (snapshot.Widgets!.Any(w => w.Visible && (w.Content == MatrixContent.Clock || w.Content == MatrixContent.Sensor || w.Content == MatrixContent.Text && w.Scroll))) _outputTimer.Start();
            Status = "Layout sent. Verify it on the physical panel; changes here need Apply again.";
        }
        catch (Exception ex) { if (_disposed) return; _applied = null; await _device.StopAsync(); Status = "Matrix output stopped: " + ex.Message; }
        finally { IsBusy = false; ConnectionChanged?.Invoke(this, EventArgs.Empty); }
    }
    public async Task StopAsync()
    {
        if (_disposed) return;
        _outputTimer.Stop(); _applied = null; await _device.StopAsync();
        Status = "Matrix stopped. The controller may retain its last visual.";
        ConnectionChanged?.Invoke(this, EventArgs.Empty); ApplyCommand.RaiseCanExecuteChanged();
    }
    public void UpdateMetrics(IReadOnlyDictionary<string, MetricValue> values)
    {
        _sample = new Dictionary<string, MetricValue>(values, StringComparer.OrdinalIgnoreCase); _sampleAt = DateTime.UtcNow;
        RaisePropertyChanged(nameof(PreviewReading)); PreviewChanged?.Invoke(this, EventArgs.Empty);
    }
    private bool TryValue(string source, out int value)
    {
        value = 0;
        if (DateTime.UtcNow - _sampleAt > TimeSpan.FromSeconds(5) || !_sample.TryGetValue(source, out var raw) || raw.Numeric is not double number || !double.IsFinite(number)) return false;
        value = (int)Math.Clamp(Math.Round(number), -1000, 1000000); return true;
    }
    private string Reading(string source) => TryValue(source, out var value) ? value + (source.EndsWith("Usage") ? "%" : "°C") : "N/A";
    public async Task TickAsync()
    {
        if (_disposed || !IsConnected || IsBusy || _heartbeatRunning || _applied is null) return;
        _heartbeatRunning = true;
        try
        {
            var applied = _applied; bool visible = true; var utc = DateTime.UtcNow;
            for (int i = 0; i < applied.Widgets!.Count; i++)
                if (applied.Widgets[i].Visible && applied.Widgets[i].Content == MatrixContent.Sensor && !TryValue(applied.Widgets[i].Source, out _)) visible = false;
            if (visible) await _device.UpdatePixelsAsync(RenderOutput(applied, utc, (Environment.TickCount64 - _scrollStarted) / 200), _lifetime.Token);
            if (!ReferenceEquals(applied, _applied) || _disposed) return;
            if (Environment.TickCount64 - _lastHeartbeat >= 1000 || visible != _wasVisible)
            {
                await _device.HeartbeatAsync(utc, [], applied.Brightness, visible, _lifetime.Token);
                _lastHeartbeat = Environment.TickCount64; _wasVisible = visible;
            }
            if (visible && Status.StartsWith("Output blanked")) Status = "Sensor readings restored. Output resumed.";
            if (!visible) Status = "Output blanked while a selected sensor is unavailable. It resumes when readings return.";
        }
        catch (Exception ex) { if (_disposed) return; _outputTimer.Stop(); _applied = null; await _device.StopAsync(); Status = "Matrix disconnected: " + ex.Message; ConnectionChanged?.Invoke(this, EventArgs.Empty); }
        finally { _heartbeatRunning = false; }
    }
    private MatrixSettings RenderOutput(MatrixSettings settings, DateTime utc, long step)
        => MatrixPixelRenderer.Render(settings, source => TryValue(source, out var value) ? value : null, utc, step);
    public byte[] PreviewPixels() => MatrixPixelRenderer.Compose(RenderOutput(_draft, DateTime.UtcNow, 0));
    public void PaintPixel(int x, int y, bool erase)
    {
        var r = SelectedContentRegion;
        if (!IsEditable || ContentKind != MatrixContent.PixelArt || x < r.X || x >= r.X + r.Width || y < r.Y || y >= r.Y + r.Height) return;
        int offset = ((y - r.Y) * 90 + x - r.X) * 3;
        var colour = erase ? SKColors.Black : SKColor.Parse(Colour);
        if (Tile.Pixels[offset] == colour.Red && Tile.Pixels[offset + 1] == colour.Green && Tile.Pixels[offset + 2] == colour.Blue) return;
        Tile.Pixels[offset] = colour.Red; Tile.Pixels[offset + 1] = colour.Green; Tile.Pixels[offset + 2] = colour.Blue;
        Changed();
    }
    private void ImportImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            using var stream = File.OpenRead(dialog.FileName); using var codec = SKCodec.Create(stream);
            if (codec is null || (long)codec.Info.Width * codec.Info.Height > 16000000) throw new InvalidDataException("Choose an image of at most 16 megapixels.");
            using var original = SKBitmap.Decode(codec); var r = SelectedContentRegion;
            using var reduced = original.Resize(new SKImageInfo(r.Width, r.Height), new SKSamplingOptions(SKFilterMode.Linear));
            if (reduced is null) throw new InvalidDataException("Could not resize the image.");
            Tile.Content = MatrixContent.PixelArt; Tile.Pixels = new byte[2700];
            for (int y = 0; y < r.Height; y++) for (int x = 0; x < r.Width; x++)
            { var c = reduced.GetPixel(x, y); int p = (y * 90 + x) * 3; Tile.Pixels[p] = c.Red; Tile.Pixels[p + 1] = c.Green; Tile.Pixels[p + 2] = c.Blue; }
            Changed(); RefreshFields();
        }
        catch (Exception ex) { Status = "Could not import image: " + ex.Message; }
    }
    public void Dispose() { if (_disposed) return; _saveTimer.Stop(); _outputTimer.Stop(); _save(Snapshot); _disposed = true; _lifetime.Cancel(); _device.Dispose(); _lifetime.Dispose(); }
}
