using Microsoft.Win32;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Rendering;
using PCInfoScreenStudio.Services;

namespace PCInfoScreenStudio.ViewModels;

public sealed partial class MainViewModel
{
    private bool _exportingDiagnostics;
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
        videoDecoders = VideoFrameProvider.RunningDecoders, videoFrameBytes = VideoFrameProvider.CachedFrameBytes
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
