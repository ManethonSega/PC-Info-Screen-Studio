using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Services;

public static class CanvasSizing
{
    public static void Resize(ThemeDocument document, DisplayCanvasSize size)
    {
        var landscape = document.Orientation == ThemeOrientation.Landscape;
        var longer = size == DisplayCanvasSize.Turing52 ? 1280 : 480;
        var shorter = size == DisplayCanvasSize.Turing52 ? 720 : 320;
        var width = landscape ? longer : shorter;
        var height = landscape ? shorter : longer;
        ResizeTo(document, width, height);
    }

    public static void ResizeTo(ThemeDocument document, int width, int height)
    {
        if (!IsSupported(width, height)) throw new ArgumentException("Unsupported display canvas size.");
        var xScale = width / (double)document.CanvasWidth;
        var yScale = height / (double)document.CanvasHeight;
        var textScale = Math.Min(xScale, yScale);
        document.Orientation = width > height ? ThemeOrientation.Landscape : ThemeOrientation.Portrait;
        document.CanvasWidth = width;
        document.CanvasHeight = height;
        foreach (var widget in document.Widgets.Concat(document.HybridWidgets))
        {
            widget.X *= xScale;
            widget.Y *= yScale;
            widget.Width *= xScale;
            widget.Height *= yScale;
            widget.FontSize *= textScale;
        }
        document.PhotoFrame.CaptionFontSize *= textScale;
    }
    public static bool IsSupported(int width, int height)
        => (width, height) is (480, 320) or (320, 480) or (1280, 720) or (720, 1280);
}
