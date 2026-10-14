using System.Windows;
using System.Collections.ObjectModel;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;

namespace PCInfoScreenStudio.ViewModels;

public sealed partial class MainViewModel
{
    private CancellationTokenSource? _fileOperationCancellation;
    private bool _fileOperationBusy;
    private bool _fileOperationAllowsEditing;
    private long _fileOperationGeneration;
    private string _fileOperationStatus = string.Empty;
    private string _recoveryNotice = string.Empty;
    public bool IsFileOperationBusy => _fileOperationBusy;
    public bool CanChangeWorkspace => !_fileOperationBusy;
    public bool CanEditContent => !_fileOperationBusy || _fileOperationAllowsEditing;
    public string RecoveryNotice => _recoveryNotice;
    public string ActivityStatus => _fileOperationBusy ? _fileOperationStatus :
        !string.IsNullOrEmpty(_recoveryNotice) ? _recoveryNotice : HardwareStatus;
    public Visibility FileOperationVisibility => _fileOperationBusy ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RecoveryNoticeVisibility => !string.IsNullOrEmpty(_recoveryNotice) && !_fileOperationBusy ? Visibility.Visible : Visibility.Collapsed;
    public RelayCommand CancelFileOperationCommand { get; private set; } = null!;
    public RelayCommand DismissRecoveryNoticeCommand { get; private set; } = null!;
    private CancellationToken FileOperationToken => _fileOperationCancellation?.Token ?? CancellationToken.None;

    private bool BeginFileOperation(string status, bool allowEditing = false)
    {
        if (_isDisposed || _fileOperationBusy) return false;
        _fileOperationBusy = true;
        _fileOperationGeneration++;
        _fileOperationAllowsEditing = allowEditing;
        _fileOperationStatus = status;
        _fileOperationCancellation = new();
        RaiseFileOperationState();
        return true;
    }

    private void EndFileOperation()
    {
        _fileOperationCancellation?.Dispose();
        _fileOperationCancellation = null;
        _fileOperationBusy = false;
        RaiseFileOperationState();
    }

    public void CancelFileOperation() => _fileOperationCancellation?.Cancel();
    private void ReportFileProgress(string message)
    {
        if (!_fileOperationBusy || _isDisposed) return;
        _fileOperationStatus = message;
        RaisePropertyChanged(nameof(ActivityStatus));
    }
    private IProgress<string> FileProgress()
    {
        var generation = _fileOperationGeneration;
        return new Progress<string>(message => { if (generation == _fileOperationGeneration) ReportFileProgress(message); });
    }
    private void AddRecoveryNotice(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || _recoveryNotice.Contains(message, StringComparison.Ordinal)) return;
        _recoveryNotice = string.IsNullOrWhiteSpace(_recoveryNotice) ? message : _recoveryNotice + " " + message;
        RaisePropertyChanged(nameof(RecoveryNotice)); RaisePropertyChanged(nameof(RecoveryNoticeVisibility)); RaisePropertyChanged(nameof(ActivityStatus));
    }
    private void RaiseFileOperationState()
    {
        foreach (var name in new[] { nameof(IsFileOperationBusy), nameof(CanChangeWorkspace), nameof(CanEditContent), nameof(CanChooseScreen), nameof(ActivityStatus), nameof(FileOperationVisibility), nameof(RecoveryNoticeVisibility) }) RaisePropertyChanged(name);
        foreach (var command in new[] { NewCommand, OpenCommand, SaveCommand, SaveAsCommand, LoadThemeCommand, DuplicateThemeCommand, RenameThemeCommand, DeleteThemeCommand,
            ImportImageCommand, ImportGifCommand, ImportVideoCommand, ImportFontCommand, SaveModeThemeCommand, LoadModeThemeCommand, DeleteModeThemeCommand, ToggleOrientationCommand,
            UndoCommand, RedoCommand, AddPhotosCommand, AddPhotoFolderCommand, CancelFileOperationCommand }) command?.RaiseCanExecuteChanged();
    }

    public async Task AddPhotoFilesAsync(IEnumerable<string> files, string? folder = null)
    {
        if (!BeginFileOperation("Reading photos...")) return;
        var workspace = Workspace; var mode = Document.Mode; var selected = files.ToArray();
        try
        {
            var prepared = await PhotoImportService.PrepareAsync(() => folder is null ? selected : Directory.EnumerateFiles(folder), FileOperationToken, FileProgress());
            FileOperationToken.ThrowIfCancellationRequested();
            if (_isDisposed || Workspace != workspace || Document.Mode != mode) return;
            var settings = Document.PhotoFrame;
            var current = settings.Photos.ToList();
            var known = current.Where(p => p.AssetId is null).Select(p => p.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = prepared.Where(p => known.Add(p.SourcePath)).ToArray();
            if (added.Length == 0 && folder is null) return;
            DetachPhotoFrame(settings);
            settings.Photos = new ObservableCollection<PhotoFrameItem>(current.Concat(added));
            if (folder is not null) settings.WatchedFolder = Path.GetFullPath(folder);
            AttachPhotoFrame(settings); RaisePropertyChanged(nameof(Document));
            if (added.Length > 0) SelectedPhoto = added[0];
            InitializePhotoFrameRuntime(); MarkDirtyAndRefresh(); RaisePhotoCommandStates();
            DeviceStatus = $"{added.Length} photos added.";
        }
        catch (OperationCanceledException) { if (!_isDisposed) DeviceStatus = "Photo import cancelled. Your playlist was kept."; }
        catch (Exception ex) { DiagnosticsService.RecordError("Photo import", ex); if (!_isDisposed) AddRecoveryNotice("Photos could not be read. Your current playlist was kept."); }
        finally { EndFileOperation(); }
    }

    public async Task LoadModeThemeFileAsync(string path)
    {
        if (!CanChangeWorkspace || !ConfirmDiscardIfNeeded() || !BeginFileOperation("Opening settings theme...")) return;
        var workspace = Workspace; var mode = Document.Mode; var revision = _editRevision;
        try
        {
            var preset = await Task.Run(() => _modeThemeService.Load(path), FileOperationToken);
            if (preset.Mode != mode || mode == ScreenMode.InfoScreen) throw new InvalidDataException("Choose the settings theme for the current screen mode.");
            var folder = preset.PhotoFrame.WatchedFolder;
            IReadOnlyList<PhotoFrameItem>? photos = null;
            var unavailable = false;
            if (!string.IsNullOrWhiteSpace(folder))
            {
                unavailable = !Directory.Exists(folder);
                if (!unavailable) photos = await PhotoImportService.PrepareAsync(() => Directory.EnumerateFiles(folder), FileOperationToken, FileProgress());
            }
            FileOperationToken.ThrowIfCancellationRequested();
            if (_isDisposed || Workspace != workspace || Document.Mode != mode || _editRevision != revision) return;
            var previousWidth = Document.CanvasWidth; var previousHeight = Document.CanvasHeight;
            _suppressDirty = true;
            try
            {
                ModeThemeService.ApplyForDisplay(Document, preset, CanvasSize);
                if (photos is not null)
                {
                    var settings = Document.PhotoFrame; DetachPhotoFrame(settings);
                    settings.Photos = new ObservableCollection<PhotoFrameItem>(photos); AttachPhotoFrame(settings); RaisePropertyChanged(nameof(Document));
                }
                InitializePhotoFrameRuntime();
                if (mode == ScreenMode.Hybrid) SelectedWidget = Document.HybridWidgets.OrderBy(w => w.ZIndex).FirstOrDefault();
            }
            finally { _suppressDirty = false; }
            if (Document.CanvasWidth != previousWidth || Document.CanvasHeight != previousHeight)
                foreach (var other in Enum.GetValues<ScreenMode>().Where(m => m != mode)) _themeSessions.MarkDirty(other);
            SetActiveThemeLoaded(path); RefreshModeThemes();
            SelectedModeTheme = ModeThemes.FirstOrDefault(t => t.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase));
            CaptureHistoryNow(); ThemeChanged?.Invoke(this, EventArgs.Empty); if (LivePreview) RequestLiveFrame?.Invoke(this, EventArgs.Empty);
            if (unavailable) AddRecoveryNotice("The settings theme loaded, but its photo folder is unavailable. Your existing playlist was kept; choose the folder's new location.");
        }
        catch (OperationCanceledException) { if (!_isDisposed) DeviceStatus = "Opening cancelled. Your current workspace was kept."; }
        catch (Exception ex) { DiagnosticsService.RecordError("Mode theme load", ex); if (!_isDisposed) AddRecoveryNotice("The settings theme could not be opened. Your current workspace was kept."); }
        finally { EndFileOperation(); }
    }
}
