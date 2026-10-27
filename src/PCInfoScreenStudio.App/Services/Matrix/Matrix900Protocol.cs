using System.Buffers.Binary;
using System.Text;
using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Services.Matrix;

// Original packet encoder from interoperability observations documented in docs/research/PHANTEKS_MATRIX900.md.
// No vendor implementation, firmware, artwork or fonts are distributed with the application.
public static class Matrix900Protocol
{
    public const int VendorId = 0x2E3C, ProductId = 0x6500;
    public static byte[] Report(int length, byte command)
    {
        if (length is < 64 or > 1024) throw new InvalidDataException("Unsupported Matrix HID report length.");
        var report = new byte[length]; report[0] = 1; report[1] = command; return report;
    }
    public static byte[] Brightness(int length, int percent, bool active = true)
    {
        var r = Report(length, 4); r[5] = 1;
        // OEM input is a percentage, mapped to its controller PWM range.
        r[6] = active && percent > 0 ? (byte)(59 + Math.Round(Math.Clamp(percent, 0, 100) * 1.21f)) : (byte)0;
        return r;
    }
    public static byte[] MatrixCount(int length, int count)
    { var r = Report(length, 5); r[2] = 255; r[5] = 1; r[6] = checked((byte)count); return r; }
    public static byte[] Text(int length, int slot, string text)
    {
        if (slot is < 0 or > 5 || text.Length > 50 || text.Any(c => c is < ' ' or > '~'))
            throw new InvalidDataException("Matrix firmware text accepts up to 50 printable ASCII characters. Use pixel art for other glyphs.");
        var r = Report(length, 2); r[5] = (byte)(text.Length + 1); r[6] = (byte)slot;
        Encoding.ASCII.GetBytes(text).CopyTo(r, 7); return r;
    }
    public static byte[] Widget(int length, int layout, int slot, MatrixTileSettings tile)
    {
        if (layout is not (1 or 3 or 9) || slot < 0 || slot >= (layout == 1 ? 1 : layout == 3 ? 3 : 6))
            throw new InvalidDataException("Invalid Matrix layout or tile.");
        var r = Report(length, 3); r[2] = (byte)(layout == 1 ? 1 : layout == 3 ? 3 : 6);
        r[4] = (byte)slot; r[5] = 41;
        var d = r.AsSpan(6, 41); d[0] = (byte)layout;
        byte red = Convert.ToByte(tile.Colour.Substring(1, 2), 16), green = Convert.ToByte(tile.Colour.Substring(3, 2), 16), blue = Convert.ToByte(tile.Colour.Substring(5, 2), 16);
        void Colour(int offset, byte rr, byte gg, byte bb) { r[offset + 6] = rr; r[offset + 7] = gg; r[offset + 8] = bb; }
        switch (tile.Content)
        {
            case MatrixContent.Text: case MatrixContent.PixelArt:
                d[1] = 4; d[2] = (byte)(tile.Content == MatrixContent.PixelArt ? 1 : 0);
                d[3] = tile.Content == MatrixContent.Text && tile.Scroll ? (byte)1 : (byte)0; d[4] = 2;
                Colour(5, red, green, blue); Colour(11, 255, 0, 0); break;
            case MatrixContent.Sensor:
                d[1] = 2; d[2] = 0;
                d[3] = (byte)MatrixTileSettings.Sources.Single(s => s.Key == tile.Source).FirmwareId;
                BinaryPrimitives.WriteUInt16BigEndian(d.Slice(12, 2), (ushort)tile.AlarmThreshold);
                d[14] = tile.Alarm ? (byte)1 : (byte)0; d[15] = 1;
                Colour(16, red, green, blue); Colour(22, 255, 0, 0); break;
            case MatrixContent.Clock:
                d[1] = 1; d[2] = 1; d[3] = 2; d[4] = 1; d[5] = d[6] = 255;
                d[9] = 12; // UTC/GMT entry in the firmware timezone table; DST disabled.
                Colour(11, red, green, blue); Colour(17, 255, 0, 0); break;
        }
        return r;
    }
    public static IReadOnlyList<byte[]> PixelPages(int length, int slot, int width, int height, byte[] rgb)
    {
        if (slot is < 0 or > 5 || width is not (30 or 60 or 90) || height is not (5 or 10) || rgb.Length != width * height * 3)
            throw new InvalidDataException("Invalid Matrix pixel payload.");
        var pages = new List<byte[]>();
        for (int offset = 0, page = 0; offset < rgb.Length; offset += 56, page++)
        {
            int size = Math.Min(56, rgb.Length - offset); var r = Report(length, 5);
            r[2] = (byte)slot; BinaryPrimitives.WriteUInt16BigEndian(r.AsSpan(3, 2), (ushort)page);
            r[5] = (byte)(size + 2); r[6] = (byte)width; r[7] = (byte)height;
            rgb.AsSpan(offset, size).CopyTo(r.AsSpan(8)); pages.Add(r);
        }
        return pages;
    }
    public static byte[] Heartbeat(int length, DateTime utcTime, IReadOnlyList<int> values)
    {
        var r = Report(length, 6); r[5] = 58;
        var wallClock = DateTime.SpecifyKind(utcTime, DateTimeKind.Utc);
        BinaryPrimitives.WriteInt32BigEndian(r.AsSpan(6, 4), checked((int)new DateTimeOffset(wallClock).ToUnixTimeSeconds()));
        for (int i = 0; i < Math.Min(values.Count, 6); i++)
        {
            int value = values[i]; r[16 + i * 6] = (byte)Math.Clamp(value, 0, 100);
            BinaryPrimitives.WriteInt32BigEndian(r.AsSpan(17 + i * 6, 4), value);
        }
        return r;
    }
}
