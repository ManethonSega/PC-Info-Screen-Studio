using System.IO;
using System.Buffers.Binary;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services.Matrix;

internal static class Matrix900ProtocolChecks
{
    public static async Task RunAsync()
    {
        const int size = 65;
        Require(Matrix900Protocol.VendorId == 0x2E3C && Matrix900Protocol.ProductId == 0x6500, "Only normal Matrix 900 HID IDs are targeted.");
        var text = Matrix900Protocol.Text(size, 0, "SEGA");
        Require(text.Take(11).SequenceEqual(new byte[] { 1, 2, 0, 0, 0, 5, 0, 0x53, 0x45, 0x47, 0x41 }) && text.Skip(11).All(b => b == 0), "Known text report fixture and zero padding.");
        var brightness = Matrix900Protocol.Brightness(size, 100);
        Require(brightness[0] == 1 && brightness[1] == 4 && brightness[5] == 1 && brightness[6] == 180 && brightness[7] == 0, "Brightness uses the observed controller mapping, with Celsius selected.");
        Require(Matrix900Protocol.Brightness(size, 0)[6] == 0 && Matrix900Protocol.Brightness(size, 50, false)[6] == 0, "Zero brightness and unavailable sensors can blank output.");
        var tile = new MatrixTileSettings { Content = MatrixContent.Text, Text = "SEGA", Colour = "#123456" };
        var widget = Matrix900Protocol.Widget(size, 3, 2, tile);
        Require(widget.Take(9).SequenceEqual(new byte[] { 1, 3, 3, 0, 2, 41, 3, 4, 0 }) && widget[9] == 1 && widget[10] == 2 &&
            widget[11] == 0x12 && widget[12] == 0x34 && widget[13] == 0x56, "Widget fixture selects three-column layout, tile 3, scrolling text and RGB bytes.");
        tile.Content = MatrixContent.Sensor; tile.Source = "GPU.Usage"; tile.Alarm = true; tile.AlarmThreshold = 73;
        widget = Matrix900Protocol.Widget(size, 1, 0, tile);
        Require(widget[9] == 30 && widget[18] == 0 && widget[19] == 73 && widget[20] == 1, "GPU load and high alarm use the correct firmware fields.");
        var heartbeat = Matrix900Protocol.Heartbeat(size, new DateTime(1970, 1, 1, 0, 0, 1, DateTimeKind.Utc), [63, 1234]);
        Require(BinaryPrimitives.ReadInt32BigEndian(heartbeat.AsSpan(6, 4)) == 1 && heartbeat[16] == 63 &&
            BinaryPrimitives.ReadInt32BigEndian(heartbeat.AsSpan(17, 4)) == 63 && heartbeat[22] == 100 &&
            BinaryPrimitives.ReadInt32BigEndian(heartbeat.AsSpan(23, 4)) == 1234, "Heartbeat timestamp, slot stride, numeric endian order and progress cap.");
        var rgb = Enumerable.Range(0, 2700).Select(i => (byte)(i % 251)).ToArray();
        var pages = Matrix900Protocol.PixelPages(size, 0, 90, 10, rgb);
        Require(pages.Count == 49 && pages[48][5] == 14 && pages[48][3] == 0 && pages[48][4] == 48, "Full panel has 48 full pages plus a 12-byte remainder.");
        Require(pages.SelectMany(p => p.Skip(8).Take(p[5] - 2)).SequenceEqual(rgb), "RGB bytes reconstruct without padding, reordering or loss.");
        Throws(() => Matrix900Protocol.Text(size, 0, new string('a', 51)));
        Throws(() => Matrix900Protocol.Text(size, 0, "Hi 👌"));
        Throws(() => Matrix900Protocol.PixelPages(size, 6, 90, 10, rgb));
        Throws(() => Matrix900Protocol.Report(12, 1));
        var repaired = new MatrixSettings { Layout = 500, Brightness = 120, Tiles = [new() { Colour = "bad", Pixels = new byte[2] }] }.ValidatedCopy();
        Require(repaired.Layout == 3 && repaired.Brightness == 100 && repaired.Tiles.Count == 6 && repaired.Tiles[0].Pixels.Length == 2700, "Bad Matrix fields recover independently.");
        Require(MatrixSettings.Region(9, 3) == (30, 5, 30, 5), "Six-tile positions follow native column-major tile indices.");
        var fake = new Connection();
        using (var device = new Matrix900Device(() => fake))
        {
            await device.ConnectAsync();
            var settings = new MatrixSettings { Layout = 1, Tiles = [new() { Content = MatrixContent.PixelArt, Pixels = rgb }] }.ValidatedCopy();
            await device.ConfigureAsync(settings);
            Require(fake.Writes.Count(r => r[1] == 5 && r[2] != 255) == 49 && fake.Reads == 52,
                "All pixel pages wait for matching acknowledgements, ignoring one wrong-tile reply.");
            Require(fake.Writes.Last()[1] == 4 && fake.Writes.Any(r => r[1] == 3), "Brightness and native widget configuration complete the upload.");
            Require(fake.Writes.All(r => r[1] is 1 or 2 or 3 or 4 or 5 or 6 or 13), "Operational output does not enter bootloader or update firmware.");
            var invalid = new MatrixSettings { Layout = 3 }; invalid.Tiles[2].Content = MatrixContent.Text; invalid.Tiles[2].Text = "👌";
            int before = fake.Writes.Count;
            try { await device.ConfigureAsync(invalid); throw new Exception("Expected text validation failure."); } catch (InvalidDataException) { }
            Require(fake.Writes.Count == before, "A bad later tile causes no partial layout writes.");
            await device.StopAsync(); Require(!device.IsConnected && fake.Disposed, "Stop releases the HID connection.");
        }
        var oldFirmware = new Connection { Version = "V1.0.0.4" };
        using (var device = new Matrix900Device(() => oldFirmware))
        {
            try { await device.ConnectAsync(); throw new Exception("Expected older firmware refusal."); } catch (IOException) { }
            Require(!device.IsConnected && oldFirmware.Disposed && oldFirmware.Writes.All(r => r[1] == 13), "Old firmware is refused before operational writes; no update commands are sent.");
        }
        var noAck = new Connection { Acknowledge = false };
        using (var device = new Matrix900Device(() => noAck))
        {
            await device.ConnectAsync();
            var settings = new MatrixSettings { Layout = 1 }; settings.Tiles[0].Content = MatrixContent.PixelArt;
            try { await device.ConfigureAsync(settings); throw new Exception("Expected missing acknowledgement failure."); } catch (IOException) { }
            Require(noAck.Writes.Count(r => r[1] == 5 && r[2] != 255) == 1, "Missing acknowledgement aborts before transmitting the next page.");
        }
        Console.WriteLine("PASS: Matrix 900 packet fixtures, source/alarm fields, heartbeat, RGB paging and ACK gating, malformed preferences, validation before I/O and HID lifetime.");
    }
    private sealed class Connection : IMatrixConnection
    {
        public int ReportLength => 65;
        public readonly List<byte[]> Writes = [];
        private readonly Queue<byte[]> _replies = new();
        public int Reads;
        public bool Disposed;
        public bool Acknowledge = true;
        public string Version = "V1.0.0.5";
        public void Write(byte[] r)
        {
            Writes.Add((byte[])r.Clone());
            if (r[1] == 13)
            {
                var text = System.Text.Encoding.ASCII.GetBytes("CHIP,BOOT,TYPE,Phanteks,Matrix900," + Version);
                var reply = new byte[65]; reply[0] = 1; reply[1] = 13; reply[5] = (byte)text.Length; text.CopyTo(reply, 6); _replies.Enqueue(reply);
            }
            if (Acknowledge && r[1] == 5 && r[2] != 255)
            {
                if (_replies.Count == 0 && r[4] == 0) _replies.Enqueue([1, 5, 4, 0, 0, 0]);
                _replies.Enqueue([1, 5, r[2], r[3], r[4], 0]);
            }
        }
        public byte[] Read() { Reads++; if (_replies.Count == 0) throw new TimeoutException(); return _replies.Dequeue(); }
        public void Dispose() => Disposed = true;
    }
    private static void Throws(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Expected validation failure."); }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
