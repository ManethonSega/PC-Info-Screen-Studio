using Microsoft.Win32;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Rendering;
using PCInfoScreenStudio.Services;

namespace PCInfoScreenStudio.ViewModels;

public sealed partial class MainViewModel
{
    private bool _exportingDiagnostics;
    private CancellationTokenSource? _videoImportCancellation;
    private string _videoImportStatus = string.Empty;
    public string VideoImportStatus { get => _videoImportStatus; private set => SetProperty(ref _videoImportStatus, value); }
    public int VideoMaximumFps => VideoPlaybackProfile.MaximumFramesPerSecond(CanvasSize);
    private async Task ImportVideoAsync()
    {
        if (!CanChangeWorkspace) return;
        var path = _dialogs.OpenVideo(); if (path is null) return;
        if (!BeginFileOperation("Importing video...")) return;
        var workspace = Workspace; var mode = Document.Mode;
        VideoImportStatus = "Importing video...";
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(FileOperationToken); _videoImportCancellation = cancellation;
        ThemeAsset? asset = null;
        try
        {
            asset = mode == ScreenMode.Hybrid
                ? await Task.Run(() => _assetService.DescribeLinkedVideo(path), cancellation.Token)
                : await _assetService.PrepareAssetAsync(workspace, path, ThemeAssetKind.Video, cancellation.Token, FileProgress());
            cancellation.Token.ThrowIfCancellationRequested();
            if (_isDisposed || Workspace != workspace || Document.Mode != mode) { VideoImportStatus = "Import cancelled because the workspace changed."; return; }
            if (mode != ScreenMode.Hybrid) workspace.Document.Assets.Add(asset);
            workspace.IsDirty = true;
            AttachImportedMedia(asset, ThemeAssetKind.Video); asset = null; VideoImportStatus = string.Empty;
        }
        catch (OperationCanceledException) { VideoImportStatus = "Video import cancelled."; }
        catch (Exception ex) { DiagnosticsService.RecordError("Video import", ex); VideoImportStatus = ex is InvalidDataException ? ex.Message : "Could not import video. Check the file and available disk space."; }
        finally
        {
            if (asset is not null && mode != ScreenMode.Hybrid && File.Exists(asset.LocalPath)) { try { File.Delete(asset.LocalPath!); } catch { } }
            _videoImportCancellation = null;
            EndFileOperation();
        }
    }
    private void CancelVideoImport() => _videoImportCancellation?.Cancel();
    public async Task ImportMediaFileAsync(string path, ThemeAssetKind kind)
    {
        if (!BeginFileOperation("Importing media...")) return;
        var workspace = Workspace; var mode = Document.Mode;
        ThemeAsset? asset = null;
        using var retained = workspace.RetainFiles();
        try
        {
            asset = kind == ThemeAssetKind.Video && mode == ScreenMode.Hybrid
                ? await Task.Run(() => _assetService.DescribeLinkedVideo(path), FileOperationToken)
                : await _assetService.PrepareAssetAsync(workspace, path, kind, FileOperationToken, FileProgress());
            FileOperationToken.ThrowIfCancellationRequested();
            if (_isDisposed || Workspace != workspace || Document.Mode != mode) return;
            if (kind == ThemeAssetKind.Font)
            {
                workspace.Document.Assets.Add(asset); FontAssets.Add(asset);
                if (SelectedWidget is not null) SelectedWidget.FontAssetId = asset.Id;
                MarkDirty();
            }
            else
            {
                if (mode != ScreenMode.Hybrid || kind != ThemeAssetKind.Video) workspace.Document.Assets.Add(asset);
                AttachImportedMedia(asset, kind);
            }
            asset = null;
        }
        catch (OperationCanceledException) { if (!_isDisposed) DeviceStatus = "Import cancelled. Your current workspace was kept."; }
        catch (Exception ex)
        {
            DiagnosticsService.RecordError("Media import", ex);
            if (!_isDisposed) DeviceStatus = "Could not import media. Check the file and available disk space.";
        }
        finally
        {
            if (asset is not null && !(kind == ThemeAssetKind.Video && mode == ScreenMode.Hybrid))
                try { File.Delete(asset.LocalPath!); } catch { }
            EndFileOperation();
        }
    }
    private string _diagnosticsStatus = string.Empty;
    public RelayCommand ExportDiagnosticsCommand { get; private set; } = null!;
    public RelayCommand RestartVideoCommand { get; private set; } = null!;
    public string DiagnosticsStatus { get => _diagnosticsStatus; private set => SetProperty(ref _diagnosticsStatus, value); }
    private void InitializeMediaCommands()
    {
        ExportDiagnosticsCommand = new RelayCommand(async () => await ExportDiagnosticsAsync(), () => !_exportingDiagnostics);
        RestartVideoCommand = new RelayCommand(parameter => {
            if (parameter is WidgetModel { Type: WidgetType.Video } widget) {
                VideoFrameProvider.Restart(Workspace, widget); widget.MediaPaused = false;
                ThemeChanged?.Invoke(this, EventArgs.Empty); if (LivePreview) RequestLiveFrame?.Invoke(this, EventArgs.Empty);
            }
        });
    }
    public object CreateDiagnosticsReport() => DiagnosticsService.CreateReport(_device.GetDiagnostics(), new {
        mode = Document.Mode.ToString(), outputMode = Document.RuntimeMode.ToString(),
        canvas = new { width = Document.CanvasWidth, height = Document.CanvasHeight, rotation = Document.DeviceRotation.ToString() },
        liveUpdates = LivePreview, editorActive = IsEditorActive, startWithWindows = StartWithWindows,
        autoStartDisplay = AutoStartDisplay, sensorElevated = IsHardwareElevated, pawnIoInstalled = IsLowLevelSensorDriverInstalled,
        widgets = Document.Widgets.Count, hybridWidgets = Document.HybridWidgets.Count, assets = Document.Assets.Count,
        photos = Document.PhotoFrame.Photos.Count, activeVideos = VideoFrameProvider.ActiveCount,
        videoDecoders = VideoFrameProvider.RunningDecoders, videoFrameBytes = VideoFrameProvider.CachedFrameBytes,
        mediaCache = MediaResourceCache.Diagnostics, fileOperationBusy = IsFileOperationBusy
    });
    private async Task ExportDiagnosticsAsync()
    {
        if (_exportingDiagnostics) return;
        var dialog = new SaveFileDialog { Title = "Export diagnostics (versions, connection, counters and error codes; no photos or personal paths)",
            Filter = "Diagnostics report (*.json)|*.json", DefaultExt = ".json", AddExtension = true,
            FileName = $"PCInfoScreenStudio-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.json", OverwritePrompt = true };
        if (dialog.ShowDialog() != true) return;
        _exportingDiagnostics = true; ExportDiagnosticsCommand.RaiseCanExecuteChanged(); DiagnosticsStatus = "Exporting diagnostics…";
        try { await DiagnosticsService.ExportAsync(dialog.FileName, CreateDiagnosticsReport()); DiagnosticsStatus = "Diagnostics saved. Attach the JSON file to your bug report."; }
        catch (Exception ex) { DiagnosticsService.RecordError("Diagnostics export", ex); DiagnosticsStatus = "Could not save diagnostics. Choose a writable folder and try again."; }
        finally { _exportingDiagnostics = false; ExportDiagnosticsCommand.RaiseCanExecuteChanged(); }
    }
}
