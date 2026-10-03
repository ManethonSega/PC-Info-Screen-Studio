using System.Globalization;
using SkiaSharp;
using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Rendering;

public sealed class ThemeRenderer
{
    public SKBitmap Render(ThemeWorkspace workspace)
    {
        var doc = workspace.Document;
        var bitmap = new SKBitmap(doc.CanvasWidth, doc.CanvasHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(ParseColor(doc.BackgroundColor));

        foreach (var widget in doc.Widgets.Where(w => w.IsVisible).OrderBy(w => w.ZIndex))
            DrawWidget(canvas, workspace, widget);

        return bitmap;
    }

    private static void DrawWidget(SKCanvas canvas, ThemeWorkspace workspace, WidgetModel w)
    {
        canvas.Save();
        canvas.Translate((float)(w.X + w.Width / 2), (float)(w.Y + w.Height / 2));
        canvas.RotateDegrees((float)w.Rotation);
        canvas.Translate((float)(-w.Width / 2), (float)(-w.Height / 2));
        canvas.ClipRect(new SKRect(0, 0, (float)w.Width, (float)w.Height));

        switch (w.Type)
        {
            case WidgetType.Text:
                DrawText(canvas, workspace, w, string.IsNullOrWhiteSpace(w.RuntimeText) ? w.Label : w.RuntimeText!, verticalCenter: true);
                break;
            case WidgetType.Value:
                DrawValue(canvas, workspace, w);
                break;
            case WidgetType.CircularGauge:
                DrawCircularGauge(canvas, workspace, w);
                break;
            case WidgetType.BarGauge:
                DrawBarGauge(canvas, workspace, w);
                break;
            case WidgetType.Graph:
                DrawGraph(canvas, workspace, w);
                break;
            case WidgetType.Image:
            case WidgetType.AnimatedImage:
                DrawImage(canvas, workspace, w);
                break;
            case WidgetType.Video:
                DrawVideoPlaceholder(canvas, workspace, w);
                break;
            case WidgetType.Shape:
                DrawShape(canvas, w);
                break;
        }
        canvas.Restore();
    }

    private static void DrawValue(SKCanvas canvas, ThemeWorkspace workspace, WidgetModel w)
    {
        FillBackground(canvas, w);
        var value = FormatValue(w);
        if (w.ShowLabel)
            DrawText(canvas, workspace, w, w.Label, y: (float)(w.FontSize + 2), size: (float)Math.Max(8, w.FontSize * .55), color: w.ForegroundColor);
        if (w.ShowValue)
            DrawText(canvas, workspace, w, value, y: (float)(w.Height - 8), size: (float)w.FontSize, color: w.AccentColor);
    }

    private static void DrawCircularGauge(SKCanvas canvas, ThemeWorkspace workspace, WidgetModel w)
    {
        FillBackground(canvas, w);
        var padding = Math.Max(4, w.GaugeThickness / 2 + 2);
        var diameter = Math.Min(w.Width, w.Height) - padding * 2;
        var left = (w.Width - diameter) / 2;
        var top = (w.Height - diameter) / 2;
        var rect = new SKRect((float)left, (float)top, (float)(left + diameter), (float)(top + diameter));

        using var track = Paint(w.SecondaryColor, (float)w.GaugeThickness, SKPaintStyle.Stroke);
        track.StrokeCap = SKStrokeCap.Round;
        canvas.DrawArc(rect, -90, 360, false, track);

        var fraction = NormalizedValue(w);
        using var active = Paint(w.AccentColor, (float)w.GaugeThickness, SKPaintStyle.Stroke);
        active.StrokeCap = SKStrokeCap.Round;
        canvas.DrawArc(rect, -90, (float)(360 * fraction), false, active);

        if (w.ShowValue)
            DrawCenteredText(canvas, workspace, w, FormatValue(w), (float)(w.FontSize * .85), w.ForegroundColor, (float)(w.Height * .55));
        if (w.ShowLabel)
            DrawCenteredText(canvas, workspace, w, w.Label, (float)Math.Max(8, w.FontSize * .4), w.ForegroundColor, (float)(w.Height * .75));
    }

    private static void DrawBarGauge(SKCanvas canvas, ThemeWorkspace workspace, WidgetModel w)
    {
        FillBackground(canvas, w);
        var header = w.ShowLabel || w.ShowValue ? Math.Min(28f, (float)w.Height * .3f) : 0f;
        if (w.ShowLabel)
            DrawText(canvas, workspace, w, w.Label, y: Math.Max(12, header - 5), size: Math.Max(8, (float)w.FontSize * .45f), color: w.ForegroundColor);
        if (w.ShowValue)
            DrawRightText(canvas, workspace, w, FormatValue(w), y: Math.Max(12, header - 5), size: Math.Max(8, (float)w.FontSize * .55f), color: w.ForegroundColor);

        var y = header + 4;
        var h = Math.Max(4, w.Height - y - 4);
        var count = Math.Max(2, w.SegmentCount);
        var gap = 2f;
        var segW = ((float)w.Width - 8 - gap * (count - 1)) / count;
        var activeCount = (int)Math.Round(count * NormalizedValue(w));
        using var off = Paint(w.SecondaryColor, style: SKPaintStyle.Fill);
        using var on = Paint(w.AccentColor, style: SKPaintStyle.Fill);
        for (var i = 0; i < count; i++)
        {
            var x = 4 + i * (segW + gap);
            var r = new SKRect(x, (float)y, x + segW, (float)(y + h));
            canvas.DrawRect(r, i < activeCount ? on : off);
        }
    }

    private static void DrawGraph(SKCanvas canvas, ThemeWorkspace workspace, WidgetModel w)
    {
        FillBackground(canvas, w);
        var header = Math.Min(30f, (float)w.Height * .25f);
        if (w.ShowLabel)
            DrawText(canvas, workspace, w, w.Label, y: Math.Max(12, header - 6), size: Math.Max(8, (float)w.FontSize * .45f), color: w.ForegroundColor);
        if (w.ShowValue)
            DrawRightText(canvas, workspace, w, FormatValue(w), y: Math.Max(12, header - 6), size: Math.Max(8, (float)w.FontSize * .55f), color: w.ForegroundColor);

        var graph = new SKRect(4, header + 2, (float)w.Width - 4, (float)w.Height - 4);
        using var border = Paint(w.SecondaryColor, 1, SKPaintStyle.Stroke);
        canvas.DrawRect(graph, border);

        if (w.ShowGrid)
        {
            using var grid = Paint(WithAlpha(ParseColor(w.SecondaryColor), 90), 1, SKPaintStyle.Stroke);
            for (var i = 1; i < 4; i++)
            {
                var gy = graph.Top + graph.Height * i / 4f;
                canvas.DrawLine(graph.Left, gy, graph.Right, gy, grid);
            }
        }

        var values = w.RuntimeSeries.Count >= 2 ? w.RuntimeSeries.ToArray() : PreviewSeries(w, 36);
        var min = w.AutoScale ? values.Min() : w.Minimum;
        var max = w.AutoScale ? values.Max() : w.Maximum;
        if (Math.Abs(max - min) < .001) max = min + 1;

        float PX(int i) => graph.Left + graph.Width * i / (values.Length - 1);
        float PY(double v) => graph.Bottom - graph.Height * (float)((v - min) / (max - min));

        using var line = Paint(w.AccentColor, (float)w.LineThickness, SKPaintStyle.Stroke);
        line.StrokeJoin = SKStrokeJoin.Round;
        line.StrokeCap = SKStrokeCap.Round;

        if (w.GraphStyle == GraphStyle.Blocks)
        {
            var bw = graph.Width / values.Length;
            using var fill = Paint(w.AccentColor, style: SKPaintStyle.Fill);
            for (var i = 0; i < values.Length; i++)
            {
                var top = PY(values[i]);
                canvas.DrawRect(new SKRect(graph.Left + i * bw + 1, top, graph.Left + (i + 1) * bw - 1, graph.Bottom), fill);
            }
            return;
        }

        using var path = new SKPath();
        path.MoveTo(PX(0), PY(values[0]));
        for (var i = 1; i < values.Length; i++)
        {
            if (w.GraphStyle == GraphStyle.SteppedLine)
            {
                path.LineTo(PX(i), PY(values[i - 1]));
                path.LineTo(PX(i), PY(values[i]));
            }
            else
                path.LineTo(PX(i), PY(values[i]));
        }

        if (w.GraphStyle == GraphStyle.FilledArea)
        {
            using var fillPath = new SKPath(path);
            fillPath.LineTo(graph.Right, graph.Bottom);
            fillPath.LineTo(graph.Left, graph.Bottom);
            fillPath.Close();
            using var fill = Paint(WithAlpha(ParseColor(w.AccentColor), 90), style: SKPaintStyle.Fill);
            canvas.DrawPath(fillPath, fill);
        }
        canvas.DrawPath(path, line);
    }

    private static void DrawShape(SKCanvas canvas, WidgetModel w)
    {
        using var fill = Paint(w.BackgroundColor, style: SKPaintStyle.Fill);
        using var stroke = Paint(w.AccentColor, (float)w.LineThickness, SKPaintStyle.Stroke);
        var rect = new SKRect(2, 2, (float)w.Width - 2, (float)w.Height - 2);
        switch (w.ShapeStyle)
        {
            case ShapeStyle.Ellipse:
                canvas.DrawOval(rect, fill); canvas.DrawOval(rect, stroke); break;
            case ShapeStyle.Line:
                canvas.DrawLine(rect.Left, rect.MidY, rect.Right, rect.MidY, stroke); break;
            case ShapeStyle.RoundedRectangle:
                canvas.DrawRoundRect(rect, (float)w.CornerRadius, (float)w.CornerRadius, fill);
                canvas.DrawRoundRect(rect, (float)w.CornerRadius, (float)w.CornerRadius, stroke); break;
            default:
                canvas.DrawRect(rect, fill); canvas.DrawRect(rect, stroke); break;
        }
    }

    private static void DrawImage(SKCanvas canvas, ThemeWorkspace workspace, WidgetModel w)
    {
        FillBackground(canvas, w);
        var asset = workspace.Document.Assets.FirstOrDefault(a => a.Id == w.AssetId);
        var path = asset is null ? null : workspace.GetAbsolutePath(asset);
        if (path is null || !File.Exists(path))
        {
            DrawCenteredText(canvas, workspace, w, "IMAGE", 12, w.ForegroundColor, (float)(w.Height / 2));
            return;
        }

        using var bitmap = SKBitmap.Decode(path);
        if (bitmap is null) return;
        var dest = new SKRect(0, 0, (float)w.Width, (float)w.Height);
        var src = SourceRectForFit(bitmap.Width, bitmap.Height, dest.Width, dest.Height, w.MediaFit);
        canvas.DrawBitmap(bitmap, src, dest);
    }

    private static void DrawVideoPlaceholder(SKCanvas canvas, ThemeWorkspace workspace, WidgetModel w)
    {
        FillBackground(canvas, w);
        using var border = Paint(w.AccentColor, 2, SKPaintStyle.Stroke);
        canvas.DrawRect(new SKRect(2, 2, (float)w.Width - 2, (float)w.Height - 2), border);
        DrawCenteredText(canvas, workspace, w, "VIDEO", Math.Max(10, (float)w.FontSize * .6f), w.ForegroundColor, (float)(w.Height / 2));
    }

    private static SKRect SourceRectForFit(int sourceW, int sourceH, float destW, float destH, MediaFit fit)
    {
        if (fit is MediaFit.Stretch or MediaFit.Fit or MediaFit.Original)
            return new SKRect(0, 0, sourceW, sourceH);
        var sourceAspect = sourceW / (float)sourceH;
        var destAspect = destW / destH;
        if (sourceAspect > destAspect)
        {
            var newW = sourceH * destAspect;
            var x = (sourceW - newW) / 2f;
            return new SKRect(x, 0, x + newW, sourceH);
        }
        var newH = sourceW / destAspect;
        var y = (sourceH - newH) / 2f;
        return new SKRect(0, y, sourceW, y + newH);
    }

    private static void FillBackground(SKCanvas canvas, WidgetModel w)
    {
        var color = ParseColor(w.BackgroundColor);
        if (color.Alpha == 0) return;
        using var paint = new SKPaint { Color = WithAlpha(color, (byte)(color.Alpha * w.Opacity)), Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawRoundRect(new SKRect(0, 0, (float)w.Width, (float)w.Height), (float)w.CornerRadius, (float)w.CornerRadius, paint);
    }

    private static void DrawText(SKCanvas canvas, ThemeWorkspace workspace, WidgetModel w, string text, bool verticalCenter = false, float? y = null, float? size = null, string? color = null)
    {
        using var typeface = Typeface(workspace, w);
        using var font = new SKFont(typeface, size ?? (float)w.FontSize);
        using var paint = new SKPaint
        {
            Color = ParseColor(color ?? w.ForegroundColor),
            IsAntialias = true
        };

        var metrics = font.Metrics;
        var baseline = y ?? (verticalCenter
            ? (float)(w.Height / 2 - (metrics.Ascent + metrics.Descent) / 2)
            : -metrics.Ascent);

        canvas.DrawText(text ?? string.Empty, 4, baseline, SKTextAlign.Left, font, paint);
    }

    private static void DrawCenteredText(SKCanvas canvas, ThemeWorkspace workspace, WidgetModel w, string text, float size, string color, float centerY)
    {
        using var typeface = Typeface(workspace, w);
        using var font = new SKFont(typeface, size);
        using var paint = new SKPaint
        {
            Color = ParseColor(color),
            IsAntialias = true
        };

        var width = font.MeasureText(text, paint);
        var metrics = font.Metrics;
        var baseline = centerY - (metrics.Ascent + metrics.Descent) / 2;
        canvas.DrawText(text, ((float)w.Width - width) / 2, baseline, SKTextAlign.Left, font, paint);
    }

    private static void DrawRightText(SKCanvas canvas, ThemeWorkspace workspace, WidgetModel w, string text, float y, float size, string color)
    {
        using var typeface = Typeface(workspace, w);
        using var font = new SKFont(typeface, size);
        using var paint = new SKPaint
        {
            Color = ParseColor(color),
            IsAntialias = true
        };

        var width = font.MeasureText(text, paint);
        canvas.DrawText(text, (float)w.Width - width - 4, y, SKTextAlign.Left, font, paint);
    }

    private static SKTypeface Typeface(ThemeWorkspace workspace, WidgetModel w)
    {
        if (w.FontAssetId is Guid id)
        {
            var asset = workspace.Document.Assets.FirstOrDefault(a => a.Id == id && a.Kind == ThemeAssetKind.Font);
            if (asset is not null)
            {
                var path = workspace.GetAbsolutePath(asset);
                if (File.Exists(path))
                    return SKTypeface.FromFile(path);
            }
        }
        var style = w.FontBold && w.FontItalic ? SKFontStyle.BoldItalic : w.FontBold ? SKFontStyle.Bold : w.FontItalic ? SKFontStyle.Italic : SKFontStyle.Normal;
        return SKTypeface.FromFamilyName(w.FontFamily, style) ?? SKTypeface.Default;
    }

    private static string FormatValue(WidgetModel w)
    {
        if (!string.IsNullOrWhiteSpace(w.RuntimeText))
            return w.RuntimeText!;
        var value = w.DisplayValue.ToString(string.IsNullOrWhiteSpace(w.ValueFormat) ? "0" : w.ValueFormat, CultureInfo.CurrentCulture);
        return value + w.Suffix;
    }

    private static double NormalizedValue(WidgetModel w)
    {
        if (w.Maximum <= w.Minimum) return 0;
        return Math.Clamp((w.DisplayValue - w.Minimum) / (w.Maximum - w.Minimum), 0, 1);
    }

    private static double[] PreviewSeries(WidgetModel w, int count)
    {
        var seed = Math.Abs(w.Id.GetHashCode() % 17) / 17.0;
        var range = Math.Max(1, w.Maximum - w.Minimum);
        var center = Math.Clamp(w.DisplayValue, w.Minimum, w.Maximum);
        var result = new double[count];
        for (var i = 0; i < count; i++)
        {
            var t = i / (double)(count - 1);
            var wave = Math.Sin((t * 3.3 + seed) * Math.PI * 2) * range * .08;
            var wave2 = Math.Sin((t * 8.1 + seed * 2) * Math.PI * 2) * range * .025;
            result[i] = Math.Clamp(center + wave + wave2, w.Minimum, w.Maximum);
        }
        result[^1] = center;
        return result;
    }

    private static SKPaint Paint(string color, float strokeWidth = 1, SKPaintStyle style = SKPaintStyle.Fill)
        => Paint(ParseColor(color), strokeWidth, style);

    private static SKPaint Paint(SKColor color, float strokeWidth = 1, SKPaintStyle style = SKPaintStyle.Fill)
        => new() { Color = color, StrokeWidth = strokeWidth, Style = style, IsAntialias = true };

    public static SKColor ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return SKColors.Transparent;
        var hex = value.Trim().TrimStart('#');
        try
        {
            if (hex.Length == 6)
            {
                var rgb = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return new SKColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
            }
            if (hex.Length == 8)
            {
                var argb = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return new SKColor((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));
            }
        }
        catch { }
        return SKColors.Magenta;
    }

    private static SKColor WithAlpha(SKColor color, byte alpha) => new(color.Red, color.Green, color.Blue, alpha);
}
