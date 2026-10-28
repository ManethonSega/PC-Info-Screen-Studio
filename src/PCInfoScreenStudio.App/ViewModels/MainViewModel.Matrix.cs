using System.Windows;
using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.ViewModels;

public sealed partial class MainViewModel
{
    public MatrixWorkspaceViewModel Matrix { get; private set; } = null!;
    private bool _matrixSelected;
    private bool _switchingDisplayTarget;
    private readonly SemaphoreSlim _displayTargetGate = new(1, 1);
    public bool IsMatrixWorkspace => _matrixSelected;
    public IReadOnlyList<DisplayTargetOption> DisplayTargets { get; } =
    [new("3.5-inch · 480 × 320", DisplayTarget.Serial35), new("5.2-inch · 1280 × 720", DisplayTarget.Turing52),
     new("Phanteks Matrix 900 · 90 × 10", DisplayTarget.Matrix900)];
    public DisplayTarget SelectedDisplayTarget
    {
        get => IsMatrixWorkspace ? DisplayTarget.Matrix900 : CanvasSize == DisplayCanvasSize.Turing52 ? DisplayTarget.Turing52 : DisplayTarget.Serial35;
        set
        {
            if (!CanChooseScreen || !Enum.IsDefined(value) || value == SelectedDisplayTarget) return;
            _matrixSelected = value == DisplayTarget.Matrix900;
            var snapshot = Matrix.Snapshot; snapshot.Selected = _matrixSelected; _settings.Matrix = snapshot;
            _autoDisplayCts?.Cancel();
            if (!IsMatrixWorkspace) CanvasSize = value == DisplayTarget.Turing52 ? DisplayCanvasSize.Turing52 : DisplayCanvasSize.Serial35;
            _switchingDisplayTarget = true;
            NotifyDisplayWorkspace();
            _ = SwitchDisplayWorkspaceAsync();
        }
    }
    public Visibility MatrixPageVisibility => IsMatrixWorkspace && !IsHardwarePage ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LcdControlsVisibility => IsMatrixWorkspace ? Visibility.Collapsed : Visibility.Visible;
    private void InitializeMatrixWorkspace()
    {
        _matrixSelected = _settings.Matrix.Selected;
        Matrix = new MatrixWorkspaceViewModel(_settings.Matrix, snapshot => { snapshot.Selected = _matrixSelected; _settings.Matrix = snapshot; }, sources: DataSources);
        Matrix.Initialize();
        Matrix.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Matrix.Status) && !_isDisposed) RaisePropertyChanged(nameof(DeviceStatus)); };
        Matrix.ConnectionChanged += (_, _) =>
        {
            if (_isDisposed) return;
            foreach (var name in new[] { nameof(IsDeviceBusy), nameof(DisplayActionLabel), nameof(DisplayBrightness), nameof(BrightnessLabel), nameof(CanChooseScreen) }) RaisePropertyChanged(name);
            ConnectCommand?.RaiseCanExecuteChanged();
        };
    }
    private async Task SwitchDisplayWorkspaceAsync()
    {
        await _displayTargetGate.WaitAsync();
        try
        {
            if (_isDisposed) return;
            if (IsMatrixWorkspace && _device.IsConnected) await _device.ConnectOrDisconnectAsync();
            else if (!IsMatrixWorkspace) await Matrix.StopAsync();
        }
        finally
        {
            _displayTargetGate.Release();
            _switchingDisplayTarget = false;
            if (!_isDisposed)
            {
                RaisePropertyChanged(nameof(IsDeviceBusy));
                RaisePropertyChanged(nameof(CanChooseScreen));
                RaisePropertyChanged(nameof(DisplayActionLabel));
                ConnectCommand.RaiseCanExecuteChanged();
            }
        }
    }
    private void NotifyDisplayWorkspace()
    {
        foreach (var name in new[] { nameof(SelectedDisplayTarget), nameof(IsMatrixWorkspace), nameof(MatrixPageVisibility), nameof(LcdControlsVisibility),
            nameof(IsEditorPage), nameof(IsCanvasActive), nameof(LivePreview), nameof(EditorPageVisibility), nameof(IsDeviceBusy), nameof(CanChooseScreen), nameof(DisplayActionLabel), nameof(DisplayBrightness), nameof(BrightnessLabel) }) RaisePropertyChanged(name);
        NewCommand.RaiseCanExecuteChanged(); OpenCommand.RaiseCanExecuteChanged(); SaveCommand.RaiseCanExecuteChanged(); SaveAsCommand.RaiseCanExecuteChanged();
        RaiseHistoryCommandStates(); ToggleEditorModeCommand.RaiseCanExecuteChanged(); ConnectCommand.RaiseCanExecuteChanged();
        EditorActivityChanged?.Invoke(this, EventArgs.Empty);
    }
}
