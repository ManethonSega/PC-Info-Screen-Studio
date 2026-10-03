using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace PCInfoScreenStudio.Services;

public readonly record struct MetricValue(double? Numeric = null, string? Text = null, string? Unit = null);

/// <summary>
/// Lightweight Windows metrics that do not require a third-party hardware monitor.
/// Temperature/fan/GPU-specific sensors are intentionally supplied by later providers.
/// </summary>
public sealed class SystemMetricsService
{
    private ulong _lastIdle;
    private ulong _lastKernel;
    private ulong _lastUser;
    private long _lastNetworkReceived;
    private long _lastNetworkSent;
    private DateTime _lastNetworkSampleUtc = DateTime.UtcNow;
    private bool _hasCpuBaseline;
    private bool _hasNetworkBaseline;

    public IReadOnlyDictionary<string, MetricValue> Sample()
    {
        var result = new Dictionary<string, MetricValue>(StringComparer.OrdinalIgnoreCase);

        SampleClock(result);
        SampleCpu(result);
        SampleMemory(result);
        SampleDisk(result);
        SampleNetwork(result);

        return result;
    }

    private static void SampleClock(IDictionary<string, MetricValue> output)
    {
        var now = DateTime.Now;
        output["Clock.Time"] = new MetricValue(Text: RegionalFormatService.FormatTime(now));
        output["Clock.Date"] = new MetricValue(Text: RegionalFormatService.FormatDate(now));
        output["Clock.Day"] = new MetricValue(Text: RegionalFormatService.FormatDay(now));
    }

    private void SampleCpu(IDictionary<string, MetricValue> output)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!GetSystemTimes(out var idleFt, out var kernelFt, out var userFt)) return;

        var idle = ToUInt64(idleFt);
        var kernel = ToUInt64(kernelFt);
        var user = ToUInt64(userFt);

        if (_hasCpuBaseline)
        {
            var idleDelta = idle - _lastIdle;
            var kernelDelta = kernel - _lastKernel;
            var userDelta = user - _lastUser;
            var total = kernelDelta + userDelta;
            if (total > 0)
            {
                var busy = total > idleDelta ? total - idleDelta : 0;
                var usage = busy * 100.0 / total;
                output["CPU.Usage"] = new MetricValue(Math.Clamp(usage, 0, 100), Unit: "%");
            }
        }

        var clock = TryGetAverageCurrentCpuMhz();
        if (clock is double mhz && mhz > 0)
            output["CPU.Clock"] = new MetricValue(mhz, Unit: " MHz");

        _lastIdle = idle;
        _lastKernel = kernel;
        _lastUser = user;
        _hasCpuBaseline = true;
    }

    private static void SampleMemory(IDictionary<string, MetricValue> output)
    {
        if (!OperatingSystem.IsWindows()) return;
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status)) return;

        var total = status.TotalPhys;
        var available = status.AvailPhys;
        var used = total >= available ? total - available : 0;
        var gib = 1024d * 1024d * 1024d;

        output["RAM.Usage"] = new MetricValue(status.MemoryLoad, Unit: "%");
        output["RAM.UsedGB"] = new MetricValue(used / gib, Unit: " GB");
        output["RAM.AvailableGB"] = new MetricValue(available / gib, Unit: " GB");
        output["RAM.TotalGB"] = new MetricValue(total / gib, Unit: " GB");
    }

    private static void SampleDisk(IDictionary<string, MetricValue> output)
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrWhiteSpace(root)) return;
            var drive = new DriveInfo(root);
            if (!drive.IsReady || drive.TotalSize <= 0) return;

            var used = drive.TotalSize - drive.AvailableFreeSpace;
            output["Disk.Usage"] = new MetricValue(used * 100.0 / drive.TotalSize, Unit: "%");
            output["Disk.FreeGB"] = new MetricValue(drive.AvailableFreeSpace / (1024d * 1024d * 1024d), Unit: " GB");
        }
        catch
        {
            // Disk may be unavailable during startup/removal; leave values absent.
        }
    }

    private void SampleNetwork(IDictionary<string, MetricValue> output)
    {
        try
        {
            long received = 0;
            long sent = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    continue;

                try
                {
                    var stats = nic.GetIPv4Statistics();
                    received += stats.BytesReceived;
                    sent += stats.BytesSent;
                }
                catch
                {
                    // Ignore a transient/virtual adapter while still sampling the others.
                }
            }

            var now = DateTime.UtcNow;
            if (_hasNetworkBaseline)
            {
                var seconds = Math.Max(0.001, (now - _lastNetworkSampleUtc).TotalSeconds);
                var receivedDelta = Math.Max(0, received - _lastNetworkReceived);
                var sentDelta = Math.Max(0, sent - _lastNetworkSent);
                output["Network.Download"] = new MetricValue(receivedDelta / seconds / (1024d * 1024d), Unit: " MB/s");
                output["Network.Upload"] = new MetricValue(sentDelta / seconds / (1024d * 1024d), Unit: " MB/s");
            }

            _lastNetworkReceived = received;
            _lastNetworkSent = sent;
            _lastNetworkSampleUtc = now;
            _hasNetworkBaseline = true;
        }
        catch
        {
            // Some virtual adapters throw while being removed. Ignore one sample.
        }
    }

    private static double? TryGetAverageCurrentCpuMhz()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                return null;

            var count = Math.Max(1, Environment.ProcessorCount);
            var size = Marshal.SizeOf<ProcessorPowerInformation>();
            var length = size * count;
            var buffer = Marshal.AllocHGlobal(length);

            try
            {
                var status = CallNtPowerInformation(ProcessorInformation, IntPtr.Zero, 0, buffer, (uint)length);
                if (status != 0)
                    return null;

                double total = 0;
                var samples = 0;
                for (var i = 0; i < count; i++)
                {
                    var ptr = IntPtr.Add(buffer, i * size);
                    var info = Marshal.PtrToStructure<ProcessorPowerInformation>(ptr);
                    if (info.CurrentMhz > 0)
                    {
                        total += info.CurrentMhz;
                        samples++;
                    }
                }

                return samples == 0 ? null : total / samples;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return null;
        }
    }

    private const int ProcessorInformation = 11;

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessorPowerInformation
    {
        public uint Number;
        public uint MaxMhz;
        public uint CurrentMhz;
        public uint MhzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint CallNtPowerInformation(
        int informationLevel,
        IntPtr inputBuffer,
        uint inputBufferLength,
        IntPtr outputBuffer,
        uint outputBufferLength);

    private static ulong ToUInt64(FileTime ft) => ((ulong)ft.High << 32) | ft.Low;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);
}
