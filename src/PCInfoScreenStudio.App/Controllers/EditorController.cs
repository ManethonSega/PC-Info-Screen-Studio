using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;

namespace PCInfoScreenStudio.Controllers;

/// <summary>Owns selection, widget defaults and canvas operations for the current editor mode.</summary>
public sealed class EditorController : ObservableObject
{
    private readonly Func<ThemeDocument> _document;
    private WidgetModel? _selectedWidget;
    private double? _alignmentGuideX;
    private double? _alignmentGuideY;

    public EditorController(Func<ThemeDocument> document) => _document = document;

    private ThemeDocument Document => _document();
    private IEnumerable<WidgetModel> AllWidgets => Document.Widgets.Concat(Document.HybridWidgets);

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

    public event Action<bool>? DocumentChanged;
    public event EventHandler? CommandStatesChanged;
    public event EventHandler? AlignmentGuidesChanged;

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
        CommandStatesChanged?.Invoke(this, EventArgs.Empty);
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
        CommandStatesChanged?.Invoke(this, EventArgs.Empty);
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

    public void DeleteSelected()
    {
        var selected = SelectedWidgets.ToArray();
        if (selected.Length == 0) return;
        SelectWidget(null);
        foreach (var widget in selected)
            Document.EditorWidgets.Remove(widget);
        DocumentChanged?.Invoke(false);
    }

    public void DuplicateSelected()
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
        CommandStatesChanged?.Invoke(this, EventArgs.Empty);
        DocumentChanged?.Invoke(true);
    }

    public void NudgeSelected(object? parameter)
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

        DocumentChanged?.Invoke(true);
    }

    public void AlignSelected(object? parameter)
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

        DocumentChanged?.Invoke(true);
    }

    public void GroupSelected()
    {
        var selected = SelectedWidgets.ToArray();
        if (selected.Length < 2) return;
        var groupId = Guid.NewGuid();
        foreach (var widget in selected)
            widget.GroupId = groupId;
        DocumentChanged?.Invoke(true);
        CommandStatesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UngroupSelected()
    {
        var selected = SelectedWidgets.ToArray();
        if (selected.Length == 0) return;
        foreach (var widget in selected)
            widget.GroupId = null;
        DocumentChanged?.Invoke(true);
        CommandStatesChanged?.Invoke(this, EventArgs.Empty);
    }

    private static double SnapCoordinate(double value, double spacing)
        => Math.Round(value / spacing, MidpointRounding.AwayFromZero) * spacing;

    public void MoveLayer(int delta)
    {
        if (SelectedWidget is null) return;

        var current = Document.EditorWidgets.IndexOf(SelectedWidget);
        if (current < 0) return;

        var target = Math.Clamp(current - delta, 0, Document.EditorWidgets.Count - 1);
        if (target == current) return;

        Document.EditorWidgets.Move(current, target);
        NormalizeZIndices();
        DocumentChanged?.Invoke(true);
    }

    public void NormalizeZIndices()
    {
        for (var i = 0; i < Document.EditorWidgets.Count; i++)
            Document.EditorWidgets[i].ZIndex = Document.EditorWidgets.Count - 1 - i;
    }

    public WidgetModel NewWidget(WidgetType type)
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

    public void AddWidgetToCanvas(WidgetType type, string? dataSource)
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
        DocumentChanged?.Invoke(true);
    }

    public static void ApplyDataSourceDefaults(WidgetModel widget)
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

    public static string FriendlyLabel(string source)
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

    public static bool IsTemperatureSource(string source)
        => source.Contains("Temperature", StringComparison.OrdinalIgnoreCase) ||
           source.Contains("Hotspot", StringComparison.OrdinalIgnoreCase) ||
           (source.Contains("[Temperature]", StringComparison.OrdinalIgnoreCase) || source.Contains("[Temp]", StringComparison.OrdinalIgnoreCase)) ||
           source.Equals("Weather.FeelsLike", StringComparison.OrdinalIgnoreCase) ||
           source.Equals("Weather.TodayHigh", StringComparison.OrdinalIgnoreCase) ||
           source.Equals("Weather.TodayLow", StringComparison.OrdinalIgnoreCase);

    public void ToggleOrientation()
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
        DocumentChanged?.Invoke(true);
    }

}

