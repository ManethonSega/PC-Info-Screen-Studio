using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Services;

public static class VideoPlaybackProfile
{
    public static int MaximumFramesPerSecond(DisplayCanvasSize display) => display == DisplayCanvasSize.Turing52 ? 20 : 5;
    // Engineering defaults, not measured physical-device frame-rate promises.
    public static int FramesPerSecond(DisplayCanvasSize display, int requested, bool automatic, double transferMs = 0)
    {
        var ceiling = MaximumFramesPerSecond(display);
        var baseline = display == DisplayCanvasSize.Turing52 ? 12 : 3;
        var fps = requested > 0 ? Math.Clamp(requested, 1, ceiling) : automatic ? baseline : ceiling;
        if (transferMs > 0) fps = Math.Min(fps, Math.Max(1, (int)Math.Floor(800 / transferMs)));
        return fps;
    }

    public static (int Width, int Height) DecodeSize(int sourceWidth, int sourceHeight, int width, int height)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || sourceWidth > 3840 || sourceHeight > 3840 || (long)sourceWidth * sourceHeight > 3840 * 2160)
            throw new InvalidDataException($"Video is {sourceWidth} × {sourceHeight}. Use up to 3840 × 2160 (4K) or portrait equivalent.");
        var scale = Math.Min(1, Math.Min(Math.Clamp(width, 2, 1280) / (double)sourceWidth, Math.Clamp(height, 2, 1280) / (double)sourceHeight));
        return (Math.Max(2, (int)(sourceWidth * scale) / 2 * 2), Math.Max(2, (int)(sourceHeight * scale) / 2 * 2));
    }
}
