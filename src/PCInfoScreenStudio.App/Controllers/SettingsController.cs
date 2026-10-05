using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;

namespace PCInfoScreenStudio.Controllers;

/// <summary>Owns preference state and persistence, independently of the editor and device.</summary>
public sealed class SettingsController : ObservableObject
{
    private readonly AppSettingsService _service;
    private readonly AppSettings _values;

    public SettingsController(AppSettingsService? service = null)
    {
        _service = service ?? new AppSettingsService();
        _values = _service.Load();
    }

    public bool FirstRunCompleted
    {
        get => _values.FirstRunCompleted;
        set => Update(_values.FirstRunCompleted, value, v => _values.FirstRunCompleted = v);
    }

    public string WeatherCity
    {
        get => _values.WeatherCity;
        set => Update(_values.WeatherCity, value, v => _values.WeatherCity = v);
    }

    public DisplayProtocolProfile DisplayProtocol
    {
        get => _values.DisplayProtocol;
        set => Update(_values.DisplayProtocol, value, v => _values.DisplayProtocol = v);
    }

    public DisplayColorMode DisplayColorMode
    {
        get => _values.DisplayColorMode;
        set => Update(_values.DisplayColorMode, value, v => _values.DisplayColorMode = v);
    }

    public bool CloseToTray
    {
        get => _values.CloseToTray;
        set => Update(_values.CloseToTray, value, v => _values.CloseToTray = v);
    }

    public bool AutoStartDisplay
    {
        get => _values.AutoStartDisplay;
        set => Update(_values.AutoStartDisplay, value, v => _values.AutoStartDisplay = v);
    }

    public bool ShowAdvancedSensors
    {
        get => _values.ShowAdvancedSensors;
        set => Update(_values.ShowAdvancedSensors, value, v => _values.ShowAdvancedSensors = v);
    }

    public bool RequestAdministratorAtStartup
    {
        get => _values.RequestAdministratorAtStartup;
        set => Update(_values.RequestAdministratorAtStartup, value, v => _values.RequestAdministratorAtStartup = v);
    }

    public bool AdvancedDisplayExpanded
    {
        get => _values.AdvancedDisplayExpanded;
        set => Update(_values.AdvancedDisplayExpanded, value, v => _values.AdvancedDisplayExpanded = v);
    }

    public bool PositionPanelExpanded
    {
        get => _values.PositionPanelExpanded;
        set => Update(_values.PositionPanelExpanded, value, v => _values.PositionPanelExpanded = v);
    }

    public bool DataPanelExpanded
    {
        get => _values.DataPanelExpanded;
        set => Update(_values.DataPanelExpanded, value, v => _values.DataPanelExpanded = v);
    }

    public bool TypographyPanelExpanded
    {
        get => _values.TypographyPanelExpanded;
        set => Update(_values.TypographyPanelExpanded, value, v => _values.TypographyPanelExpanded = v);
    }

    public bool GraphPanelExpanded
    {
        get => _values.GraphPanelExpanded;
        set => Update(_values.GraphPanelExpanded, value, v => _values.GraphPanelExpanded = v);
    }

    public bool GaugePanelExpanded
    {
        get => _values.GaugePanelExpanded;
        set => Update(_values.GaugePanelExpanded, value, v => _values.GaugePanelExpanded = v);
    }

    public bool MediaPanelExpanded
    {
        get => _values.MediaPanelExpanded;
        set => Update(_values.MediaPanelExpanded, value, v => _values.MediaPanelExpanded = v);
    }

    public bool ShapePanelExpanded
    {
        get => _values.ShapePanelExpanded;
        set => Update(_values.ShapePanelExpanded, value, v => _values.ShapePanelExpanded = v);
    }

    public bool ColoursPanelExpanded
    {
        get => _values.ColoursPanelExpanded;
        set => Update(_values.ColoursPanelExpanded, value, v => _values.ColoursPanelExpanded = v);
    }

    public double CanvasZoom
    {
        get => Math.Clamp(_values.CanvasZoom, 0.5, 3.0);
        set
        {
            var zoom = Math.Clamp(value, 0.5, 3.0);
            if (Math.Abs(_values.CanvasZoom - zoom) < .001) return;
            Update(_values.CanvasZoom, zoom, v => _values.CanvasZoom = v);
        }
    }

    public int LastPhotoIndex
    {
        get => _values.LastPhotoIndex;
        set => Update(_values.LastPhotoIndex, value, v => _values.LastPhotoIndex = v);
    }

    public bool PhotoFramePlaying
    {
        get => _values.PhotoFramePlaying;
        set => Update(_values.PhotoFramePlaying, value, v => _values.PhotoFramePlaying = v);
    }

    private void Update<T>(T current, T value, Action<T> assign,
        [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return;
        assign(value);
        _service.Save(_values);
        RaisePropertyChanged(propertyName);
    }
}
