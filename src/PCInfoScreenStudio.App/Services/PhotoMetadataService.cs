using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Services;

public static class PhotoMetadataService
{
    public static void Populate(PhotoFrameItem item, string path)
    {
        item.DisplayName = Path.GetFileNameWithoutExtension(path);
        try
        {
            using var image = Image.FromFile(path);
            item.DateTaken = ReadAscii(image, 0x9003) ?? ReadAscii(image, 0x0132) ?? string.Empty;
            var latitude = ReadGps(image, 0x0002, 0x0001);
            var longitude = ReadGps(image, 0x0004, 0x0003);
            if (latitude is not null && longitude is not null)
                item.Location = string.Create(CultureInfo.InvariantCulture, $"{latitude.Value:0.#####}, {longitude.Value:0.#####}");
        }
        catch
        {
            // Metadata is optional. The photo itself can still be used.
        }
    }

    private static string? ReadAscii(Image image, int id)
    {
        try
        {
            return System.Text.Encoding.ASCII.GetString(image.GetPropertyItem(id).Value).Trim('\0', ' ');
        }
        catch { return null; }
    }

    private static double? ReadGps(Image image, int coordinateId, int referenceId)
    {
        try
        {
            var coordinate = image.GetPropertyItem(coordinateId).Value;
            if (coordinate.Length < 24) return null;
            static double Rational(byte[] data, int offset)
            {
                var numerator = BitConverter.ToUInt32(data, offset);
                var denominator = BitConverter.ToUInt32(data, offset + 4);
                return denominator == 0 ? 0 : numerator / (double)denominator;
            }
            var value = Rational(coordinate, 0) + Rational(coordinate, 8) / 60d + Rational(coordinate, 16) / 3600d;
            var reference = ReadAscii(image, referenceId);
            if (reference is "S" or "W") value = -value;
            return value;
        }
        catch { return null; }
    }
}
