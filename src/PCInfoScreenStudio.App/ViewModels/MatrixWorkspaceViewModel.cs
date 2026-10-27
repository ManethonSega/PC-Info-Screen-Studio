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
    private int _selectedTile;
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
        _draft = settings.ValidatedCopy(); _save = save; _device = device ?? new Matrix900Device();
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); _save(Snapshot); };
        _outputTimer.Tick += async (_, _) => await TickAsync();
        ApplyCommand = new RelayCommand(() => _ = ApplyAsync(), () => IsConnected && !IsBusy && Validation.Length == 0);
        ImportImageCommand = new RelayCommand(ImportImage, () => !IsBusy);
        ClearPixelsCommand = new RelayCommand(() => { Tile.Pixels = new byte[2700]; Changed(); });
    }
    public IReadOnlyList<MatrixLayoutOption> Layouts { get; } =
        [new("One panel · 90 × 10", 1), new("Three columns · 30 × 10", 3), new("Two rows · three columns", 9)];
    public IReadOnlyList<MatrixContentOption> ContentKinds { get; } = [new("Text", MatrixContent.Text), new("Sensor", MatrixContent.Sensor), new("Clock (UTC)", MatrixContent.Clock), new("Pixel art", MatrixContent.PixelArt)];
    public IReadOnlyList<MatrixSourceOption> Sources => MatrixTileSettings.Sources;
    public IReadOnlyList<MatrixRowHeightOption> RowHeights { get; } =
        [new("4 + 4 dots · 2-dot gap", 4), new("5 + 5 dots · no gap", 5),
         new("4 + 5 dots · 1-dot gap", 45), new("5 + 4 dots · 1-dot gap", 54)];
    public int RowHeight { get => _draft.RowHeight; set { if (value is not (4 or 5 or 45 or 54) || value == RowHeight) return; _draft.RowHeight = value; RefreshFields(); Changed(); } }
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
    public MatrixAlignment Alignment { get => Tile.Alignment; set { if (!Enum.IsDefined(value) || value == Alignment) return; Tile.Alignment = value; RaisePropertyChanged(); Changed(); } }
    public Visibility AlignmentVisibility => ContentKind != MatrixContent.PixelArt ? Visibility.Visible : Visibility.Collapsed;
    public MatrixFourDotFont FourDotFont { get => Tile.FourDotFont ?? MatrixFourDotFont.Mem; set { if (!Enum.IsDefined(value) || value == FourDotFont) return; Tile.FourDotFont = value; RefreshFields(); Changed(); } }
    public MatrixFiveDotFont FiveDotFont { get => Tile.FiveDotFont ?? MatrixFiveDotFont.TomThumb; set { if (!Enum.IsDefined(value) || value == FiveDotFont) return; Tile.FiveDotFont = value; RefreshFields(); Changed(); } }
    public MatrixTenDotFont TenDotFont { get => Tile.TenDotFont ?? MatrixTenDotFont.Pixel5x5Double; set { if (!Enum.IsDefined(value) || value == TenDotFont) return; Tile.TenDotFont = value; RefreshFields(); Changed(); } }
    public Visibility FourDotFontVisibility => FontVisibility(4);
    public Visibility FiveDotFontVisibility => FontVisibility(5);
    public Visibility TenDotFontVisibility => FontVisibility(10);
    private Visibility FontVisibility(int height) => ContentKind != MatrixContent.PixelArt && SelectedContentRegion.Height == height ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RowHeightVisibility => Layout == 9 ? Visibility.Visible : Visibility.Collapsed;
    public string FontNote => ContentKind == MatrixContent.PixelArt ? ""
        : SelectedContentRegion.Height == 5 && FiveDotFont == MatrixFiveDotFont.Mem5x6 ? "Five-dot capitals; Q is fitted within five rows."
        : SelectedContentRegion.Height == 5 && FiveDotFont is MatrixFiveDotFont.Petty5H or MatrixFiveDotFont.Petty5x5 ? "Uppercase font. Lowercase text uses capitals."
        : SelectedContentRegion.Height == 10 && TenDotFont != MatrixTenDotFont.Pixel5x5Double ? "Tall adaptation: height doubled, width unchanged." : "";
    public int TileCount => _draft.Count;
    public string SelectedTileLabel => $"Tile {SelectedTile + 1} selected";
    public (int X, int Y, int Width, int Height) SelectedContentRegion => _draft.ContentRegion(SelectedTile);
    public MatrixSettings Snapshot => _draft.ValidatedCopy();
    public MatrixTileSettings Tile => _draft.Tiles[_selectedTile];
    public int Layout { get => _draft.Layout; set { if (value == Layout || value is not (1 or 3 or 9)) return; _draft.Layout = value; _selectedTile = 0; Changed(); RefreshFields(); } }
    public int SelectedTile { get => _selectedTile; set { if (value < 0 || value >= _draft.Count || value == _selectedTile) return; _selectedTile = value; RefreshFields(); PreviewChanged?.Invoke(this, EventArgs.Empty); } }
    public MatrixContent ContentKind { get => Tile.Content; set { if (!Enum.IsDefined(value) || value == Tile.Content) return; Tile.Content = value; Changed(); RefreshFields(); } }
    public string Text { get => Tile.Text; set { Tile.Text = value ?? ""; RaisePropertyChanged(); Changed(); } }
    public string Source { get => Tile.Source; set { if (!Sources.Any(s => s.Key == value)) return; Tile.Source = value; RaisePropertyChanged(); Changed(); } }
    public string Colour { get => Tile.Colour; set { if (value?.Length == 9) value = "#" + value[3..]; if (!System.Text.RegularExpressions.Regex.IsMatch(value ?? "", "^#[0-9a-fA-F]{6}$")) return; Tile.Colour = value!; RaisePropertyChanged(); Changed(); } }
    public bool Scroll { get => Tile.Scroll; set { Tile.Scroll = value; RaisePropertyChanged(); Changed(); } }
    public bool Alarm { get => Tile.Alarm; set { Tile.Alarm = value; RaisePropertyChanged(); Changed(); } }
    public int AlarmThreshold { get => Tile.AlarmThreshold; set { Tile.AlarmThreshold = Math.Clamp(value, 0, 100); RaisePropertyChanged(); Changed(); } }
    public int Brightness { get => _draft.Brightness; set { _draft.Brightness = Math.Clamp(value, 0, 100); if (_applied is not null) _applied.Brightness = _draft.Brightness; RaisePropertyChanged(); Changed(); ConnectionChanged?.Invoke(this, EventArgs.Empty); } }
    public bool IsConnected => _device.IsConnected;
    public string? FirmwareVersion => _device.FirmwareVersion;
    public bool IsBusy { get => _busy; private set { _busy = value; RaisePropertyChanged(); ApplyCommand.RaiseCanExecuteChanged(); ImportImageCommand.RaiseCanExecuteChanged(); RaisePropertyChanged(nameof(FontNote)); ConnectionChanged?.Invoke(this, EventArgs.Empty); } }
    public string Status { get => _status; private set { _status = value; RaisePropertyChanged(); } }
    public string Validation
    {
        get
        {
            for (int i = 0; i < _draft.Count; i++)
            {
                var tile = _draft.Tiles[i];
                if (tile.Content == MatrixContent.Text && (tile.Text.Length > 50 || tile.Text.Any(c => c is < ' ' or > '~')))
                    return "Matrix text supports 50 printable ASCII characters per tile. Use pixel art for other glyphs.";

            }
            return "";
        }
    }
    public Visibility TextVisibility => ContentKind == MatrixContent.Text ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SensorVisibility => ContentKind == MatrixContent.Sensor ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PixelVisibility => ContentKind == MatrixContent.PixelArt ? Visibility.Visible : Visibility.Collapsed;
    public string PreviewReading => Tile.Content == MatrixContent.Sensor ? Reading(Tile.Source) : Tile.Content == MatrixContent.Clock ? DateTime.UtcNow.ToString("HH:mm") : Tile.Text;
    public RelayCommand ApplyCommand { get; }
    public RelayCommand ImportImageCommand { get; }
    public RelayCommand ClearPixelsCommand { get; }
    public void Initialize() => RefreshFields();
    private void RefreshFields()
    {
        foreach (var name in new[] { nameof(Layout), nameof(RowHeight), nameof(FourDotFont), nameof(FiveDotFont), nameof(TenDotFont), nameof(RowHeightVisibility), nameof(FourDotFontVisibility), nameof(FiveDotFontVisibility), nameof(TenDotFontVisibility), nameof(FontNote), nameof(Alignment), nameof(AlignmentVisibility), nameof(SelectedTile), nameof(SelectedTileLabel), nameof(ContentKind), nameof(Text), nameof(Source), nameof(Colour), nameof(Scroll), nameof(Alarm), nameof(AlarmThreshold), nameof(Brightness), nameof(TextVisibility), nameof(SensorVisibility), nameof(PixelVisibility), nameof(PreviewReading), nameof(Validation) }) RaisePropertyChanged(name);
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
        for (int i = 0; i < snapshot.Count; i++)
            if (snapshot.Tiles[i].Content == MatrixContent.Sensor && !TryValue(snapshot.Tiles[i].Source, out _))
            { Status = "A selected sensor has no current reading. Choose an available source or enable full sensors before applying."; return; }
        _outputTimer.Stop(); _applied = null;
        IsBusy = true; Status = "Sending layout to the Matrix...";
        try
        {
            await _device.ConfigureAsync(RenderOutput(snapshot, DateTime.UtcNow, 0), _lifetime.Token); _applied = snapshot;
            _scrollStarted = Environment.TickCount64; _lastHeartbeat = 0; _wasVisible = true;
            if (Enumerable.Range(0, snapshot.Count).Any(i => snapshot.Tiles[i].Content == MatrixContent.Text && snapshot.Tiles[i].Scroll)) _outputTimer.Start();
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
            for (int i = 0; i < applied.Count; i++)
                if (applied.Tiles[i].Content == MatrixContent.Sensor && !TryValue(applied.Tiles[i].Source, out _)) visible = false;
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
        var r = _draft.ContentRegion(SelectedTile); var native = MatrixSettings.Region(Layout, SelectedTile);
        if (ContentKind != MatrixContent.PixelArt || x < r.X || x >= r.X + r.Width || y < r.Y || y >= r.Y + r.Height) return;
        int offset = ((y - native.Y) * native.Width + x - native.X) * 3;
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
            using var original = SKBitmap.Decode(codec); var r = _draft.ContentRegion(SelectedTile); var native = MatrixSettings.Region(Layout, SelectedTile);
            using var reduced = original.Resize(new SKImageInfo(r.Width, r.Height), new SKSamplingOptions(SKFilterMode.Linear));
            if (reduced is null) throw new InvalidDataException("Could not resize the image.");
            Tile.Content = MatrixContent.PixelArt; Tile.Pixels = new byte[2700];
            for (int y = 0; y < r.Height; y++) for (int x = 0; x < r.Width; x++)
            { var c = reduced.GetPixel(x, y); int p = ((y + r.Y - native.Y) * r.Width + x) * 3; Tile.Pixels[p] = c.Red; Tile.Pixels[p + 1] = c.Green; Tile.Pixels[p + 2] = c.Blue; }
            Changed(); RefreshFields();
        }
        catch (Exception ex) { Status = "Could not import image: " + ex.Message; }
    }
    public void Dispose() { if (_disposed) return; _saveTimer.Stop(); _outputTimer.Stop(); _save(Snapshot); _disposed = true; _lifetime.Cancel(); _device.Dispose(); _lifetime.Dispose(); }
}
