using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Services;

public static class VideoPlaybackProfile
{
    // Engineering defaults, not measured physical-device frame-rate promises.
    public static int FramesPerSecond(DisplayCanvasSize display, int requested, bool automatic, double transferMs = 0)
    {
        var ceiling = display == DisplayCanvasSize.Turing52 ? 20 : 5;
        var baseline = display == DisplayCanvasSize.Turing52 ? 12 : 3;
        var fps = requested > 0 ? Math.Clamp(requested, 1, ceiling) : automatic ? baseline : ceiling;
        if (transferMs > 0) fps = Math.Min(fps, Math.Max(1, (int)Math.Floor(800 / transferMs)));
        return fps;
    }

    public static (int Width, int Height) DecodeSize(int sourceWidth, int sourceHeight, int width, int height)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || sourceWidth > 1920 || sourceHeight > 1920 || (long)sourceWidth * sourceHeight > 1920 * 1080)
            throw new InvalidDataException("Use a video up to 1920 × 1080 (or portrait equivalent).");
        var scale = Math.Min(1, Math.Min(Math.Clamp(width, 2, 1280) / (double)sourceWidth, Math.Clamp(height, 2, 1280) / (double)sourceHeight));
        return (Math.Max(2, (int)(sourceWidth * scale) / 2 * 2), Math.Max(2, (int)(sourceHeight * scale) / 2 * 2));
    }
}
