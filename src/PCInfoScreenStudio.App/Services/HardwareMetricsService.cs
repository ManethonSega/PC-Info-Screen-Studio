using System.Management;
using System.Security.Principal;
using Hwinfo.SharedMemory;
using LibreHardwareMonitor.Hardware;

namespace PCInfoScreenStudio.Services;

public sealed class HardwareMetricsService : IDisposable
{
    private readonly object _sync = new();
    private Computer? _computer;
    private SharedMemoryReader? _hwInfoReader;
    private bool _disposed;

    public string Status { get; private set; } = "Not initialized";
    public bool IsElevated => IsAdministrator();

    public bool IsLowLevelDriverInstalled
    {
        get
        {
            try
            {
                return LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled;
            }
            catch
            {
                return false;
            }
        }
    }

    public IReadOnlyDictionary<string, MetricValue> Sample()
    {
        lock (_sync)
        {
            if (_disposed)
                return new Dictionary<string, MetricValue>(StringComparer.OrdinalIgnoreCase);

            try
            {
                EnsureComputer();

                var sensors = new List<SensorSnapshot>();
                foreach (var hardware in _computer!.Hardware)
                    CollectHardware(hardware, hardware.HardwareType.ToString(), sensors);

                var result = BuildMetrics(sensors);

                var hwInfoCount = 0;
                var hwInfoActive = TryMergeHwInfo(result, out hwInfoCount);

                if (!result.ContainsKey("Disk.Temperature"))
                {
                    var storageTemp = TryReadWindowsStorageTemperature();
                    if (storageTemp is double temp)
                        result["Disk.Temperature"] = new MetricValue(temp, Unit: "°C");
                }

                var elevated = IsAdministrator();
                var missingLowLevel =
                    !result.ContainsKey("CPU.Temperature") ||
                    !result.ContainsKey("CPU.Power") ||
                    !result.ContainsKey("Disk.Temperature");

                var sources = $"LibreHardwareMonitor: {sensors.Count} sensors";
                if (hwInfoActive)
                    sources += $" · HWiNFO: {hwInfoCount} readings";

                if (missingLowLevel && !IsLowLevelDriverInstalled)
                    Status = $"{sources} · full CPU/motherboard sensors need the optional PawnIO hardware-access driver";
                else if (missingLowLevel && !elevated)
                    Status = $"{sources} · some CPU/storage sensors need Administrator access";
                else
                    Status = $"{sources} · {(elevated ? "elevated" : "standard access")}";

                return result;
            }
            catch (Exception ex)
            {
                Status = "Hardware sensors unavailable: " + ex.GetBaseException().Message;
                return new Dictionary<string, MetricValue>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    private void EnsureComputer()
    {
        if (_computer is not null)
            return;

        var lowLevel = IsLowLevelDriverInstalled;

        // Keep driver-independent providers alive even when PawnIO is not yet
        // installed. Enabling motherboard/controller groups without the driver
        // can make one low-level initialization failure hide otherwise healthy
        // GPU and storage telemetry.
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = lowLevel,
            IsControllerEnabled = lowLevel,
            IsStorageEnabled = true,
            IsNetworkEnabled = false,
            IsPowerMonitorEnabled = false,
            IsPsuEnabled = false
        };

        _computer.Open();
    }

    private static void CollectHardware(IHardware hardware, string rootType, ICollection<SensorSnapshot> output)
    {
        try
        {
            hardware.Update();
        }
        catch
        {
            // One inaccessible controller must not hide all other sensors.
        }

        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.Value is float value && float.IsFinite(value))
            {
                output.Add(new SensorSnapshot(
                    RootType: rootType,
                    HardwareName: hardware.Name ?? string.Empty,
                    SensorName: sensor.Name ?? string.Empty,
                    SensorType: sensor.SensorType.ToString(),
                    Value: value));
            }
        }

        foreach (var subHardware in hardware.SubHardware)
            CollectHardware(subHardware, rootType, output);
    }

    private static Dictionary<string, MetricValue> BuildMetrics(IReadOnlyList<SensorSnapshot> sensors)
    {
        var output = new Dictionary<string, MetricValue>(StringComparer.OrdinalIgnoreCase);

        // Follow the same general priority used by the established Turing projects:
        // Core Average -> CPU Package -> Core Max -> another real core temperature.
        Add(output, "CPU.Temperature",
            PickPreferred(
                sensors, IsCpu, "Temperature",
                ["cpu package", "package", "core average", "core max", "tctl", "tdie", "core"],
                1, 125),
            "°C");

        Add(output, "CPU.Power",
            PickPreferred(
                sensors, IsCpu, "Power",
                ["cpu package", "package power", "package", "cpu cores", "cores", "cpu"],
                0.05, 1000),
            " W");

        Add(output, "CPU.Clock",
            Average(
                sensors, IsCpu, "Clock",
                s => s.Value > 100 &&
                     !ContainsAny(s.SensorName, "bus", "bclk") &&
                     ContainsAny(s.SensorName, "core", "cpu") &&
                     !ContainsAny(s.SensorName, "effective")),
            " MHz");

        Add(output, "GPU.Usage",
            PickPreferred(
                sensors, IsGpu, "Load",
                ["gpu core", "core", "d3d 3d", "3d"],
                0, 100),
            "%");

        Add(output, "GPU.Temperature",
            PickPreferred(
                sensors, IsGpu, "Temperature",
                ["gpu core", "core", "edge", "temperature"],
                1, 130),
            "°C");

        Add(output, "GPU.Hotspot",
            PickPreferred(
                sensors, IsGpu, "Temperature",
                ["hot spot", "hotspot", "junction"],
                1, 150,
                requireNameMatch: true),
            "°C");

        // Prefer used/total VRAM. This is what the mature Turing implementations
        // do and is more reliable on AMD cards than a generic "memory load" sensor.
        var gpuMemoryUsed = PickByNameAnyType(
            sensors, IsGpu, ["SmallData", "Data"],
            ["gpu memory used", "dedicated memory used", "d3d dedicated memory used", "memory used"]);

        var gpuMemoryTotal = PickByNameAnyType(
            sensors, IsGpu, ["SmallData", "Data"],
            ["gpu memory total", "dedicated memory total", "d3d dedicated memory total", "memory total"]);

        double? gpuMemoryPercent = null;
        if (gpuMemoryUsed is double used && gpuMemoryTotal is double total && total > 0)
        {
            gpuMemoryPercent = Math.Clamp(used / total * 100d, 0d, 100d);
            output["GPU.VRAMUsed"] = new MetricValue(ToMemoryMegabytes(used), Unit: " MB");
            output["GPU.VRAMTotal"] = new MetricValue(ToMemoryMegabytes(total), Unit: " MB");
        }

        gpuMemoryPercent ??= PickPreferred(
            sensors, IsGpu, "Load",
            ["gpu memory", "vram", "dedicated memory"],
            0, 100,
            requireNameMatch: true);

        Add(output, "GPU.VRAM", gpuMemoryPercent, "%");

        Add(output, "GPU.Power",
            PickPreferred(
                sensors, IsGpu, "Power",
                ["gpu package", "total board", "board power", "asic power", "gpu power", "gpu"],
                0.05, 1000),
            " W");

        Add(output, "GPU.FanRPM",
            PickPreferred(
                sensors, IsGpu, "Fan",
                ["gpu fan", "fan"],
                1, 20000),
            " RPM");

        var storageTemps = sensors
            .Where(s => IsStorage(s) &&
                        s.SensorType.Equals("Temperature", StringComparison.OrdinalIgnoreCase) &&
                        s.Value is > 0 and < 150)
            .Select(s => (double)s.Value)
            .ToArray();

        if (storageTemps.Length > 0)
            output["Disk.Temperature"] = new MetricValue(storageTemps.Max(), Unit: "°C");

        var pump = sensors
            .Where(s => IsCoolingHardware(s) &&
                        s.SensorType.Equals("Fan", StringComparison.OrdinalIgnoreCase) &&
                        s.Value > 0 &&
                        ContainsAny(s.SensorName, "pump", "aio", "water", "liquid"))
            .OrderByDescending(s => ScoreName(s.SensorName, ["aio pump", "pump", "water pump", "water", "liquid"]))
            .FirstOrDefault();

        if (pump is not null)
            output["Cooling.PumpRPM"] = new MetricValue(pump.Value, Unit: " RPM");

        var fans = sensors
            .Where(s => IsCoolingHardware(s) &&
                        s.SensorType.Equals("Fan", StringComparison.OrdinalIgnoreCase) &&
                        s.Value > 0 &&
                        !ContainsAny(s.SensorName, "pump", "aio", "water", "liquid"))
            .OrderByDescending(s => ScoreName(s.SensorName, ["cpu fan", "cpu", "chassis", "system", "fan"]))
            .ThenBy(s => s.SensorName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        if (fans.Length > 0)
        {
            output["Cooling.FanRPM"] = new MetricValue(fans[0].Value, Unit: " RPM");

            for (var i = 0; i < Math.Min(6, fans.Length); i++)
                output[$"Cooling.Fan{i + 1}RPM"] = new MetricValue(fans[i].Value, Unit: " RPM");
        }

        foreach (var sensor in sensors)
        {
            var key = BuildRawSourceKey(sensor);
            if (!output.ContainsKey(key))
                output[key] = new MetricValue(sensor.Value, Unit: UnitFor(sensor.SensorType, sensor.SensorName));
        }

        return output;
    }

    private bool TryMergeHwInfo(IDictionary<string, MetricValue> output, out int readingCount)
    {
        readingCount = 0;

        try
        {
            _hwInfoReader ??= new SharedMemoryReader(new SharedMemoryReaderOptions
            {
                ReuseUnchangedPolls = true,
                MutexTimeout = TimeSpan.FromMilliseconds(100)
            });

            if (!_hwInfoReader.TryReadLocal(out var snapshot))
                return false;

            var readings = snapshot.Readings.ToArray();
            readingCount = readings.Length;

            AddHwInfoRawSources(output, readings);

            if (!HasUseful(output, "CPU.Temperature"))
            {
                var temp = HwiPick(
                    readings,
                    IsHwInfoCpu,
                    Hwinfo.SharedMemory.SensorType.Temp,
                    ["cpu package", "package", "core average", "core max", "tctl", "tdie", "core"],
                    1, 125);

                Add(output, "CPU.Temperature", temp, "°C");
            }

            if (!HasUseful(output, "CPU.Power"))
            {
                var power = HwiPick(
                    readings,
                    IsHwInfoCpu,
                    Hwinfo.SharedMemory.SensorType.Power,
                    ["cpu package power", "package power", "cpu package", "ia cores power", "cpu power"],
                    0.05, 1000);

                Add(output, "CPU.Power", power, " W");
            }

            if (!HasUseful(output, "Disk.Temperature"))
            {
                var diskTemp = readings
                    .Where(r => r.ReadingType == Hwinfo.SharedMemory.SensorType.Temp &&
                                IsHwInfoStorage(r) &&
                                r.Value is > 0 and < 150)
                    .OrderByDescending(r => HwInfoStorageScore(r))
                    .ThenByDescending(r => HwInfoLabelScore(r, ["drive temperature", "composite temperature", "temperature"]))
                    .Select(r => (double?)r.Value)
                    .FirstOrDefault();

                Add(output, "Disk.Temperature", diskTemp, "°C");
            }

            if (!HasUseful(output, "GPU.VRAM"))
            {
                var percent = readings
                    .Where(r => IsHwInfoGpu(r) &&
                                r.ReadingType == Hwinfo.SharedMemory.SensorType.Usage &&
                                r.Value is >= 0 and <= 100 &&
                                ContainsAny(HwInfoLabel(r), "memory", "vram", "dedicated"))
                    .OrderByDescending(r => HwInfoLabelScore(r, ["gpu memory usage", "memory usage", "vram usage", "dedicated memory usage"]))
                    .Select(r => (double?)r.Value)
                    .FirstOrDefault();

                if (percent is null)
                {
                    var used = HwiDataValue(readings, ["gpu memory allocated", "gpu memory used", "dedicated memory used", "d3d memory dedicated"]);
                    var total = HwiDataValue(readings, ["gpu memory total", "dedicated memory total", "total gpu memory"]);

                    if (used is double usedValue && total is double totalValue && totalValue > 0)
                        percent = Math.Clamp(usedValue / totalValue * 100d, 0d, 100d);
                }

                Add(output, "GPU.VRAM", percent, "%");
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void AddHwInfoRawSources(
        IDictionary<string, MetricValue> output,
        IEnumerable<SensorReading> readings)
    {
        foreach (var reading in readings)
        {
            if (!double.IsFinite(reading.Value))
                continue;

            var sensorName = Clean(HwInfoSensorName(reading));
            var label = Clean(HwInfoLabel(reading));
            var type = reading.ReadingType.ToString();
            var key = $"Sensor: HWiNFO / {sensorName} / {label} [{type}]";

            if (!output.ContainsKey(key))
                output[key] = new MetricValue(reading.Value, Unit: NormalizeHwInfoUnit(reading.Unit));
        }
    }

    private static double? HwiPick(
        IEnumerable<SensorReading> readings,
        Func<SensorReading, bool> hardwareFilter,
        Hwinfo.SharedMemory.SensorType type,
        IReadOnlyList<string> preferredLabels,
        double min,
        double max)
    {
        var matches = readings
            .Where(r => hardwareFilter(r) &&
                        r.ReadingType == type &&
                        double.IsFinite(r.Value) &&
                        r.Value >= min &&
                        r.Value <= max)
            .Select(r => new { Reading = r, Score = HwInfoLabelScore(r, preferredLabels) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Reading.Value)
            .ToArray();

        return matches.Length == 0 ? null : matches[0].Reading.Value;
    }

    private static double? HwiDataValue(
        IEnumerable<SensorReading> readings,
        IReadOnlyList<string> preferredLabels)
    {
        var matches = readings
            .Where(r => IsHwInfoGpu(r) &&
                        double.IsFinite(r.Value) &&
                        r.Value >= 0)
            .Select(r => new { Reading = r, Score = HwInfoLabelScore(r, preferredLabels) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ToArray();

        return matches.Length == 0 ? null : matches[0].Reading.Value;
    }

    private static int HwInfoLabelScore(SensorReading reading, IReadOnlyList<string> preferences)
        => ScoreName(HwInfoLabel(reading), preferences);

    private static int HwInfoStorageScore(SensorReading reading)
    {
        var sensor = HwInfoSensorName(reading);
        if (sensor.Contains("[C:]", StringComparison.OrdinalIgnoreCase)) return 10;
        if (sensor.Contains("S.M.A.R.T.", StringComparison.OrdinalIgnoreCase)) return 6;
        if (sensor.Contains("Drive:", StringComparison.OrdinalIgnoreCase)) return 5;
        return 0;
    }

    private static bool IsHwInfoCpu(SensorReading reading)
    {
        var sensor = HwInfoSensorName(reading);
        return sensor.StartsWith("CPU", StringComparison.OrdinalIgnoreCase) ||
               sensor.Contains("Intel Core", StringComparison.OrdinalIgnoreCase) ||
               sensor.Contains("AMD Ryzen", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHwInfoGpu(SensorReading reading)
        => HwInfoSensorName(reading).Contains("GPU", StringComparison.OrdinalIgnoreCase);

    private static bool IsHwInfoStorage(SensorReading reading)
    {
        var sensor = HwInfoSensorName(reading);
        return sensor.Contains("S.M.A.R.T.", StringComparison.OrdinalIgnoreCase) ||
               sensor.StartsWith("Drive:", StringComparison.OrdinalIgnoreCase) ||
               sensor.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ||
               sensor.Contains("SSD", StringComparison.OrdinalIgnoreCase);
    }

    private static string HwInfoSensorName(SensorReading reading)
        => !string.IsNullOrWhiteSpace(reading.Sensor.NameUser)
            ? reading.Sensor.NameUser
            : reading.Sensor.NameOrig;

    private static string HwInfoLabel(SensorReading reading)
        => !string.IsNullOrWhiteSpace(reading.LabelUser)
            ? reading.LabelUser
            : reading.LabelOrig;

    private static string NormalizeHwInfoUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
            return string.Empty;

        return unit switch
        {
            "°C" => "°C",
            "%" => "%",
            "W" => " W",
            "RPM" => " RPM",
            "MHz" => " MHz",
            "GHz" => " GHz",
            "MB" => " MB",
            "GB" => " GB",
            _ => " " + unit.Trim()
        };
    }

    private static double? TryReadWindowsStorageTemperature()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Connect();

            using var searcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery("SELECT DeviceId, Temperature FROM MSFT_StorageReliabilityCounter"));

            var values = new List<double>();
            using var collection = searcher.Get();

            foreach (ManagementObject item in collection)
            {
                if (item["Temperature"] is null)
                    continue;

                if (double.TryParse(
                        Convert.ToString(item["Temperature"], System.Globalization.CultureInfo.InvariantCulture),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var value) &&
                    value is > 0 and < 150)
                {
                    values.Add(value);
                }
            }

            return values.Count == 0 ? null : values.Max();
        }
        catch
        {
            return null;
        }
    }

    private static bool HasUseful(IDictionary<string, MetricValue> output, string key)
        => output.TryGetValue(key, out var value) &&
           value.Numeric is double numeric &&
           double.IsFinite(numeric) &&
           numeric > 0.001;

    private static string BuildRawSourceKey(SensorSnapshot sensor)
        => $"Sensor: LibreHardwareMonitor / {sensor.RootType} / {Clean(sensor.HardwareName)} / {Clean(sensor.SensorName)} [{sensor.SensorType}]";

    private static string Clean(string value)
        => string.Join(" ", value.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static string UnitFor(string sensorType, string sensorName)
    {
        if (sensorType.Equals("Temperature", StringComparison.OrdinalIgnoreCase)) return "°C";
        if (sensorType.Equals("Fan", StringComparison.OrdinalIgnoreCase)) return " RPM";
        if (sensorType.Equals("Clock", StringComparison.OrdinalIgnoreCase)) return " MHz";
        if (sensorType.Equals("Power", StringComparison.OrdinalIgnoreCase)) return " W";
        if (sensorType.Equals("Load", StringComparison.OrdinalIgnoreCase)) return "%";
        if (sensorType.Equals("Voltage", StringComparison.OrdinalIgnoreCase)) return " V";
        if (sensorType.Equals("Current", StringComparison.OrdinalIgnoreCase)) return " A";
        if (sensorType.Equals("Control", StringComparison.OrdinalIgnoreCase)) return "%";
        if (sensorType.Equals("Throughput", StringComparison.OrdinalIgnoreCase)) return " MB/s";
        if ((sensorType.Equals("Data", StringComparison.OrdinalIgnoreCase) ||
             sensorType.Equals("SmallData", StringComparison.OrdinalIgnoreCase)) &&
            sensorName.Contains("memory", StringComparison.OrdinalIgnoreCase))
            return " MB";
        return string.Empty;
    }

    private static double ToMemoryMegabytes(double value)
    {
        // LibreHardwareMonitor's GPU Memory Used/Total Data sensors are bytes,
        // while some D3D SmallData providers expose MB directly.
        return value > 1024d * 1024d
            ? value / (1024d * 1024d)
            : value;
    }

    private static void Add(
        IDictionary<string, MetricValue> output,
        string key,
        double? value,
        string unit)
    {
        if (value is double number && double.IsFinite(number))
            output[key] = new MetricValue(number, Unit: unit);
    }

    private static double? PickPreferred(
        IEnumerable<SensorSnapshot> sensors,
        Func<SensorSnapshot, bool> hardwareFilter,
        string sensorType,
        IReadOnlyList<string> preferredNames,
        double min,
        double max,
        bool requireNameMatch = false)
    {
        var matches = sensors
            .Where(s => hardwareFilter(s) &&
                        s.SensorType.Equals(sensorType, StringComparison.OrdinalIgnoreCase) &&
                        s.Value >= min &&
                        s.Value <= max)
            .Select(s => new { Sensor = s, Score = ScoreName(s.SensorName, preferredNames) })
            .Where(x => !requireNameMatch || x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Sensor.Value)
            .ToArray();

        return matches.Length == 0 ? null : matches[0].Sensor.Value;
    }

    private static double? PickByNameAnyType(
        IEnumerable<SensorSnapshot> sensors,
        Func<SensorSnapshot, bool> hardwareFilter,
        IReadOnlyCollection<string> sensorTypes,
        IReadOnlyList<string> preferredNames)
    {
        var matches = sensors
            .Where(s => hardwareFilter(s) &&
                        sensorTypes.Any(type => s.SensorType.Equals(type, StringComparison.OrdinalIgnoreCase)) &&
                        s.Value >= 0)
            .Select(s => new { Sensor = s, Score = ScoreName(s.SensorName, preferredNames) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ToArray();

        return matches.Length == 0 ? null : matches[0].Sensor.Value;
    }

    private static double? Average(
        IEnumerable<SensorSnapshot> sensors,
        Func<SensorSnapshot, bool> hardwareFilter,
        string sensorType,
        Func<SensorSnapshot, bool> sensorFilter)
    {
        var values = sensors
            .Where(s => hardwareFilter(s) &&
                        s.SensorType.Equals(sensorType, StringComparison.OrdinalIgnoreCase) &&
                        sensorFilter(s))
            .Select(s => (double)s.Value)
            .Where(double.IsFinite)
            .ToArray();

        return values.Length == 0 ? null : values.Average();
    }

    private static int ScoreName(string name, IReadOnlyList<string> preferences)
    {
        for (var i = 0; i < preferences.Count; i++)
        {
            if (name.Contains(preferences[i], StringComparison.OrdinalIgnoreCase))
                return preferences.Count - i;
        }

        return 0;
    }

    private static bool ContainsAny(string value, params string[] terms)
        => terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static bool IsCpu(SensorSnapshot sensor)
        => sensor.RootType.Equals("Cpu", StringComparison.OrdinalIgnoreCase);

    private static bool IsGpu(SensorSnapshot sensor)
        => sensor.RootType.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase);

    private static bool IsStorage(SensorSnapshot sensor)
        => sensor.RootType.Equals("Storage", StringComparison.OrdinalIgnoreCase);

    private static bool IsCoolingHardware(SensorSnapshot sensor)
        => sensor.RootType.Equals("Motherboard", StringComparison.OrdinalIgnoreCase) ||
           sensor.RootType.Equals("Controller", StringComparison.OrdinalIgnoreCase) ||
           sensor.RootType.Equals("Cooler", StringComparison.OrdinalIgnoreCase) ||
           sensor.RootType.Equals("SuperIO", StringComparison.OrdinalIgnoreCase) ||
           sensor.RootType.Equals("EmbeddedController", StringComparison.OrdinalIgnoreCase);

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;

            try { _computer?.Close(); } catch { }
            _computer = null;

            try { _hwInfoReader?.Dispose(); } catch { }
            _hwInfoReader = null;
        }
    }

    private sealed record SensorSnapshot(
        string RootType,
        string HardwareName,
        string SensorName,
        string SensorType,
        float Value);
}
