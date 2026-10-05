using System.Collections.ObjectModel;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;

namespace PCInfoScreenStudio.ViewModels;

public sealed record HardwareMetricRow(string Label, string Value);

public sealed class HardwareCardViewModel : ObservableObject
{
    private string _hardwareName = "Detecting hardware…";
    private string _value = "Unavailable";
    public HardwareCardViewModel(string title, string icon, string accent, string valueLabel)
    {
        Title = title; Icon = icon; Accent = accent; ValueLabel = valueLabel;
    }
    public string Title { get; }
    public string Icon { get; }
    public string Accent { get; }
    public string ValueLabel { get; }
    public string HardwareName { get => _hardwareName; set => SetProperty(ref _hardwareName, value); }
    public string Value { get => _value; set => SetProperty(ref _value, value); }
    public ObservableCollection<HardwareMetricRow> Rows { get; } = [];
    public void SetRows(IEnumerable<HardwareMetricRow> rows)
    {
        var next = rows.ToArray();
        if (Rows.SequenceEqual(next)) return;
        Rows.Clear();
        foreach (var row in next) Rows.Add(row);
    }
}

/// <summary>Formats existing readings for the app dashboard; owns no polling loop.</summary>
public sealed class HardwareDashboardViewModel : ObservableObject
{
    private IReadOnlyDictionary<string, MetricValue> _sample = new Dictionary<string, MetricValue>();
    private IReadOnlyDictionary<string, MetricValue> _inventory = new Dictionary<string, MetricValue>();
    private string _updatedLabel = "Waiting for the first reading";
    public ObservableCollection<HardwareCardViewModel> Cards { get; } =
    [
        new("CPU", "\uE950", "#81A8FF", "Processor usage"),
        new("GPU", "\uE7F4", "#B79AFF", "Graphics usage"),
        new("RAM", "\uE964", "#64D6BF", "Memory usage"),
        new("Storage", "\uE7F1", "#E7B67D", "System drive usage"),
        new("Network", "\uE968", "#7CC8ED", "Download"),
        new("Cooling", "\uE9CA", "#D1A2CB", "Detected fan readings")
    ];
    public string UpdatedLabel { get => _updatedLabel; private set => SetProperty(ref _updatedLabel, value); }

    public void SetInventory(IReadOnlyDictionary<string, MetricValue> inventory)
    {
        _inventory = inventory;
        Populate();
    }

    public void Update(IReadOnlyDictionary<string, MetricValue> sample)
    {
        _sample = new Dictionary<string, MetricValue>(sample, StringComparer.OrdinalIgnoreCase);
        Populate();
        UpdatedLabel = "Updated " + DateTime.Now.ToString("T", RegionalFormatService.Culture);
    }

    private void Populate()
    {
        var cpu = Cards[0];
        cpu.HardwareName = Name("Hardware.CPUName", "Processor name unavailable");
        cpu.Value = Reading("CPU.Usage");
        cpu.SetRows([Row("Temperature", "CPU.Temperature"), Row("Clock", "CPU.Clock"), Row("Power", "CPU.Power")]);
        var gpu = Cards[1];
        gpu.HardwareName = Name("Hardware.GPUName", "Graphics adapter name unavailable");
        gpu.Value = Reading("GPU.Usage");
        gpu.SetRows([Row("Temperature", "GPU.Temperature"), Row("VRAM used", "GPU.VRAMUsed"), Row("VRAM total", "GPU.VRAMTotal"), Row("Power", "GPU.Power")]);
        var ram = Cards[2];
        ram.HardwareName = _sample.ContainsKey("RAM.TotalGB") ? Reading("RAM.TotalGB") + " installed" : "System memory";
        ram.Value = Reading("RAM.Usage");
        ram.SetRows([Row("Used", "RAM.UsedGB"), Row("Available", "RAM.AvailableGB"), Row("Total", "RAM.TotalGB")]);
        var storage = Cards[3];
        storage.HardwareName = Name("Hardware.StorageNames", "Drive names unavailable");
        storage.Value = Reading("Disk.Usage");
        var storageRows = new List<HardwareMetricRow> { Row("System drive free", "Disk.FreeGB"), Row("Highest detected temperature", "Disk.Temperature") };
        foreach (var key in _sample.Keys.Where(k => k.StartsWith("Storage.Volume.", StringComparison.Ordinal) && k.EndsWith(".Usage", StringComparison.Ordinal)).OrderBy(k => k))
        {
            var prefix = key[..^6];
            var drive = prefix["Storage.Volume.".Length..];
            storageRows.Add(new HardwareMetricRow(drive, Reading(key) + " used · " + Reading(prefix + ".FreeGB") + " free"));
        }
        storage.SetRows(storageRows);
        var network = Cards[4];
        network.HardwareName = Name("Network.AdapterNames", "No active network adapter");
        network.Value = Reading("Network.Download");
        network.SetRows([Row("Upload", "Network.Upload"), new("Scope", "Combined active adapters")]);
        var cooling = Cards[5];
        var fans = _sample.Where(p => p.Key.StartsWith("Sensor: ", StringComparison.Ordinal) && p.Value.Numeric is double n && double.IsFinite(n) &&
            (p.Key.EndsWith("[Fan]", StringComparison.OrdinalIgnoreCase) || p.Value.Unit?.Trim().Equals("RPM", StringComparison.OrdinalIgnoreCase) == true))
            .OrderBy(p => p.Key).Take(8).Select(p => new HardwareMetricRow(SensorLabel(p.Key), Reading(p.Key))).ToArray();
        cooling.HardwareName = "Fans and pump";
        cooling.Value = fans.Length == 0 ? "Unavailable" : fans.Length.ToString(RegionalFormatService.Culture);
        cooling.SetRows(fans.Length > 0 ? fans : [Row("Fan", "Cooling.FanRPM"), Row("Pump", "Cooling.PumpRPM")]);
    }

    private HardwareMetricRow Row(string label, string source) => new(label, Reading(source));
    private string Name(string key, string fallback)
        => _sample.TryGetValue(key, out var live) && !string.IsNullOrWhiteSpace(live.Text) ? live.Text
            : _inventory.TryGetValue(key, out var cached) && !string.IsNullOrWhiteSpace(cached.Text) ? cached.Text : fallback;
    private string Reading(string key)
    {
        if (!_sample.TryGetValue(key, out var value)) return "Unavailable";
        if (!string.IsNullOrWhiteSpace(value.Text)) return value.Text;
        if (value.Numeric is not double number || !double.IsFinite(number)) return "Unavailable";
        if (key.Contains("Temperature", StringComparison.OrdinalIgnoreCase))
            return RegionalFormatService.ConvertTemperatureFromCelsius(number).ToString("0.#", RegionalFormatService.Culture) + RegionalFormatService.TemperatureSuffix;
        return number.ToString(Math.Abs(number) >= 1000 ? "0" : "0.#", RegionalFormatService.Culture) + " " + (value.Unit ?? "").Trim();
    }
    private static string SensorLabel(string source)
    {
        var parts = source.Split(" / ");
        return parts.Length >= 2 ? parts[^2] + " / " + parts[^1].Split(" [")[0] : source;
    }
}
