using System.Buffers.Binary;
using System.Diagnostics;
using HidSharp;
using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Services.Matrix;

public interface IMatrixConnection : IDisposable
{
    int ReportLength { get; }
    void Write(byte[] report);
    byte[] Read();
}

/// <summary>One serialized HID transaction at a time. Pixel pages advance only after a matching acknowledgement.</summary>
public sealed class Matrix900Device : IDisposable
{
    private readonly Func<IMatrixConnection> _open;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IMatrixConnection? _connection;
    private bool _disposed;
    private MatrixSettings? _pixelFrame;
    public bool IsConnected => _connection is not null;
    public string? FirmwareVersion { get; private set; }
    public Matrix900Device(Func<IMatrixConnection>? open = null) => _open = open ?? OpenHardware;

    private static IMatrixConnection OpenHardware()
    {
        // HidSharp API: https://github.com/IntergatedCircuits/HidSharp/blob/main-2.6/HidSharp/HidDevice.cs
        var candidates = DeviceList.Local.GetHidDevices(Matrix900Protocol.VendorId, Matrix900Protocol.ProductId)
            .Where(d => d.GetMaxOutputReportLength() is >= 64 and <= 1024 && d.GetMaxInputReportLength() is >= 6 and <= 1024).ToArray();
        if (candidates.Length == 0) throw new IOException("Matrix 900 not found. Check its SATA power and internal USB connection.");
        if (candidates.Length > 1) throw new IOException("Multiple Matrix HID interfaces were found. Disconnect the extra Matrix device before starting.");
        return new HidConnection(candidates[0]);
    }
    public Task ConnectAsync(CancellationToken token = default) => RunAsync(() =>
    {
        if (_connection is not null) return;
        var connection = _open();
        try
        {
            connection.Write(Matrix900Protocol.Report(connection.ReportLength, 13));
            var watch = Stopwatch.StartNew();
            Version? version = null;
            while (watch.Elapsed < TimeSpan.FromSeconds(2))
            {
                token.ThrowIfCancellationRequested();
                byte[] reply;
                try { reply = connection.Read(); } catch (TimeoutException) { continue; }
                if (reply.Length < 6 || reply[0] != 1 || reply[1] != 13 || reply[3] != 0 || reply[4] != 0 || reply[5] > reply.Length - 6) continue;
                var fields = System.Text.Encoding.ASCII.GetString(reply, 6, reply[5]).TrimEnd('\0').Split(',');
                if (fields.Length < 6) continue;
                var text = fields[5].Trim().TrimStart('V', 'v').Split('_')[0];
                if (Version.TryParse(text, out version)) break;
            }
            if (version is null) throw new IOException("Could not read Matrix firmware. Close NexLinq and reconnect the display.");
            if (version <= new Version(1, 0, 0, 4)) throw new IOException("This Matrix firmware uses the older telemetry format. Update it with NexLinq before using this experimental driver.");
            FirmwareVersion = version.ToString(); _connection = connection;
        }
        catch { connection.Dispose(); throw; }
    }, token);
    public Task StopAsync(CancellationToken token = default) => RunAsync(() =>
    { var connection = _connection; _connection = null; connection?.Dispose(); FirmwareVersion = null; _pixelFrame = null; }, token);

    public Task ConfigureAsync(MatrixSettings settings, CancellationToken token = default)
    {
        var snapshot = settings.ValidatedCopy();
        return RunAsync(() =>
        {
            var c = RequireConnection();
            var art = Enumerable.Range(0, snapshot.Count).Where(i => snapshot.Tiles[i].Content == MatrixContent.PixelArt).ToArray();
            // Validate every text before any device write, so an invalid later tile cannot partly replace a layout.
            for (int i = 0; i < snapshot.Count; i++)
                if (snapshot.Tiles[i].Content == MatrixContent.Text) _ = Matrix900Protocol.Text(c.ReportLength, i, snapshot.Tiles[i].Text);
            _pixelFrame = null;
            if (art.Length > 0)
                for (int stale = 0; stale < 64; stale++)
                { token.ThrowIfCancellationRequested(); try { _ = c.Read(); } catch (TimeoutException) { break; } }
            c.Write(Matrix900Protocol.MatrixCount(c.ReportLength, art.Length));
            token.WaitHandle.WaitOne(100); token.ThrowIfCancellationRequested();
            foreach (int slot in art)
            {
                WritePixels(c, snapshot, slot, token);
            }
            for (int i = 0; i < snapshot.Count; i++)
                if (snapshot.Tiles[i].Content == MatrixContent.Text)
                { c.Write(Matrix900Protocol.Text(c.ReportLength, i, snapshot.Tiles[i].Text)); token.WaitHandle.WaitOne(50); }
            for (int i = 0; i < snapshot.Count; i++)
            {
                token.ThrowIfCancellationRequested(); c.Write(Matrix900Protocol.Widget(c.ReportLength, snapshot.Layout, i, snapshot.Tiles[i]));
                token.WaitHandle.WaitOne(50);
            }
            token.ThrowIfCancellationRequested(); c.Write(Matrix900Protocol.Brightness(c.ReportLength, snapshot.Brightness));
            if (art.Length == snapshot.Count) _pixelFrame = snapshot;
        }, token);
    }
    public Task UpdatePixelsAsync(MatrixSettings pixels, CancellationToken token = default)
    {
        var snapshot = pixels.ValidatedCopy();
        return RunAsync(() =>
        {
            var c = RequireConnection();
            if (_pixelFrame is null || _pixelFrame.Layout != snapshot.Layout ||
                Enumerable.Range(0, snapshot.Count).Any(i => snapshot.Tiles[i].Content != MatrixContent.PixelArt))
                throw new InvalidDataException("Apply the pixel layout before updating it.");
            for (int i = 0; i < snapshot.Count; i++)
            {
                var r = MatrixSettings.Region(snapshot.Layout, i); int count = r.Width * r.Height * 3;
                if (!snapshot.Tiles[i].Pixels.AsSpan(0, count).SequenceEqual(_pixelFrame.Tiles[i].Pixels.AsSpan(0, count)))
                    WritePixels(c, snapshot, i, token);
            }
            _pixelFrame = snapshot;
        }, token);
    }
    private static void WritePixels(IMatrixConnection c, MatrixSettings frame, int slot, CancellationToken token)
    {
        var region = MatrixSettings.Region(frame.Layout, slot);
        var bytes = frame.Tiles[slot].Pixels.AsSpan(0, region.Width * region.Height * 3).ToArray();
        var pages = Matrix900Protocol.PixelPages(c.ReportLength, slot, region.Width, region.Height, bytes);
        for (int page = 0; page < pages.Count; page++)
        { token.ThrowIfCancellationRequested(); c.Write(pages[page]); WaitForPage(c, slot, page, token); }
    }
    private static void WaitForPage(IMatrixConnection connection, int slot, int page, CancellationToken token)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(2))
        {
            token.ThrowIfCancellationRequested();
            byte[] report;
            try { report = connection.Read(); } catch (TimeoutException) { continue; }
            if (report.Length >= 6 && report[0] == 1 && report[1] == 5 && report[2] == slot &&
                BinaryPrimitives.ReadUInt16BigEndian(report.AsSpan(3, 2)) == page) return;
        }
        throw new IOException($"Matrix pixel transfer timed out at tile {slot + 1}, page {page + 1}. Close NexLinq and try again.");
    }
    public Task HeartbeatAsync(DateTime localTime, IReadOnlyList<int> values, int brightness, bool visible, CancellationToken token = default)
        => RunAsync(() =>
        {
            var c = RequireConnection();
            c.Write(Matrix900Protocol.Brightness(c.ReportLength, brightness, visible));
            if (visible) c.Write(Matrix900Protocol.Heartbeat(c.ReportLength, localTime, values));
        }, token);
    private IMatrixConnection RequireConnection() => _connection ?? throw new IOException("Start the Matrix display first.");
    private async Task RunAsync(Action action, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { ObjectDisposedException.ThrowIf(_disposed, this); await Task.Run(action, token).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
    public void Dispose()
    {
        _gate.Wait();
        try { if (_disposed) return; _disposed = true; _connection?.Dispose(); _connection = null; }
        finally { _gate.Release(); }
    }
    private sealed class HidConnection : IMatrixConnection
    {
        private readonly HidStream _stream;
        private readonly int _inputLength;
        public int ReportLength { get; }
        public HidConnection(HidDevice device)
        {
            var options = new OpenConfiguration(); options.SetOption(OpenOption.Exclusive, true);
            _stream = device.Open(options); _stream.ReadTimeout = 250; _stream.WriteTimeout = 1000;
            ReportLength = device.GetMaxOutputReportLength(); _inputLength = device.GetMaxInputReportLength();
        }
        public void Write(byte[] report) => _stream.Write(report);
        public byte[] Read()
        { var data = new byte[_inputLength]; int count = _stream.Read(data); return data.AsSpan(0, count).ToArray(); }
        public void Dispose() => _stream.Dispose();
    }
}
