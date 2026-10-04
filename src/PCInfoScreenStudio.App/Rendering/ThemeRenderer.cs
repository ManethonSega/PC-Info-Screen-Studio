using System.Globalization;
using SkiaSharp;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;

namespace PCInfoScreenStudio.Rendering;

public sealed class ThemeRenderer
{
    public static void ClearCaches() => RenderResourceCache.Clear();

    public SKBitmap Render(ThemeWorkspace workspace)
    {
        var doc = workspace.Document;
        var bitmap = new SKBitmap(doc.CanvasWidth, doc.CanvasHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        if (doc.RuntimeMode == RuntimeScreenMode.Off)
        {
            canvas.Clear(SKColors.Black);
            return bitmap;
        }

        var photoMode = doc.RuntimeMode is RuntimeScreenMode.PhotoFrame or RuntimeScreenMode.Hybrid;
        if (photoMode)
            DrawPhotoFrame(canvas, workspace);
        else
            canvas.Clear(ParseColor(doc.BackgroundColor));

        if (doc.RuntimeMode is RuntimeScreenMode.InfoScreen or RuntimeScreenMode.Hybrid)
            foreach (var widget in (doc.RuntimeMode == RuntimeScreenMode.Hybrid ? doc.HybridWidgets : doc.Widgets).Where(w => w.IsVisible).OrderBy(w => w.ZIndex))
                DrawWidget(canvas, workspace, widget);

        return bitmap;
    }

    private static void DrawPhotoFrame(SKCanvas canvas, ThemeWorkspace workspace)
    {
        var doc = workspace.Document;
        var settings = doc.PhotoFrame;
        canvas.Clear(ParseColor(settings.BackgroundColor));
        if (settings.Photos.Count == 0)
        {
            using var font = new SKFont(SKTypeface.Default, 18);
            using var paint = new SKPaint { Color = SKColors.LightGray, IsAntialias = true };
            const string message = "ADD PHOTOS";
            var width = font.MeasureText(message, paint);
            canvas.DrawText(message, (doc.CanvasWidth - width) / 2, doc.CanvasHeight / 2f, SKTextAlign.Left, font, paint);
            return;
        }

        var currentIndex = Math.Clamp(settings.RuntimeCurrentIndex, 0, settings.Photos.Count - 1);
        var currentItem = settings.Photos[currentIndex];
        var current = PreparePhoto(workspace, currentItem);
        if (current is null) return;

        var destination = new SKRect(0, 0, doc.CanvasWidth, doc.CanvasHeight);
        var progress = (float)Math.Clamp(settings.RuntimeTransitionProgress, 0, 1);
        var transition = settings.RuntimeTransition;
        SKBitmap? previous = null;
        if (settings.RuntimePreviousIndex >= 0 && settings.RuntimePreviousIndex < settings.Photos.Count)
            previous = PreparePhoto(workspace, settings.Photos[settings.RuntimePreviousIndex]);

        if (previous is null || progress >= 1 || transition == PhotoTransition.Instant)
        {
            DrawBitmapAlpha(canvas, current, destination, 255);
        }
        else
        {
            switch (transition)
            {
                case PhotoTransition.Slide:
                    var previousDestination = destination;
                    previousDestination.Offset(-doc.CanvasWidth * progress, 0);
                    var currentDestination = destination;
                    currentDestination.Offset(doc.CanvasWidth * (1 - progress), 0);
                    canvas.DrawBitmap(previous, previousDestination);
                    canvas.DrawBitmap(current, currentDestination);
                    break;
                case PhotoTransition.Zoom:
                    DrawBitmapAlpha(canvas, previous, destination, (byte)(255 * (1 - progress)));
                    var scale = .82f + .18f * progress;
                    var zoomRect = ScaleAroundCenter(destination, scale);
                    DrawBitmapAlpha(canvas, current, zoomRect, (byte)(255 * progress));
                    break;
                default:
                    DrawBitmapAlpha(canvas, previous, destination, (byte)(255 * (1 - progress)));
                    DrawBitmapAlpha(canvas, current, destination, (byte)(255 * progress));
                    break;
            }
        }

        if (settings.ShowCaptions)
            DrawPhotoCaption(canvas, settings, currentItem, doc.CanvasWidth, doc.CanvasHeight);
    }

    private static SKBitmap? PreparePhoto(ThemeWorkspace workspace, PhotoFrameItem item)
    {
        var settings = workspace.Document.PhotoFrame;
        var path = ResolvePhotoPath(workspace, item);
        if (path is null || !File.Exists(path)) return null;
        return RenderResourceCache.GetPreparedPhoto(
            path,
            workspace.Document.CanvasWidth,
            workspace.Document.CanvasHeight,
            settings.Fit,
            settings.BackgroundColor);
    }

    internal static string? ResolvePhotoPath(ThemeWorkspace workspace, PhotoFrameItem item)
    {
        if (item.AssetId is Guid assetId)
        {
            var asset = workspace.Document.Assets.FirstOrDefault(a => a.Id == assetId);
            return asset is null ? null : workspace.GetAbsolutePath(asset);
        }
        return item.SourcePath;
    }

    internal static void PreloadPhoto(ThemeWorkspace workspace, PhotoFrameItem item)
        => _ = PreparePhoto(workspace, item);

    private static void DrawPhotoCaption(SKCanvas canvas, PhotoFrameSettings settings, PhotoFrameItem item, int width, int height)
    {
        var caption = item.GetCaption(settings.CaptionMode, settings.CustomCaption);
        if (string.IsNullOrWhiteSpace(caption)) return;
        var size = (float)settings.CaptionFontSize;
        using var typeface = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;
        using var font = new SKFont(typeface, size);
        using var textPaint = new SKPaint { Color = ParseColor(settings.CaptionColor), IsAntialias = true };
        var measured = Math.Min(width - 20, font.MeasureText(caption, textPaint));
        var barHeight = size + 18;
        using var background = new SKPaint { Color = new SKColor(0, 0, 0, 155), IsAntialias = true };
        canvas.DrawRoundRect(new SKRect(8, height - barHeight - 8, width - 8, height - 8), 6, 6, background);
        canvas.Save();
        canvas.ClipRect(new SKRect(14, height - barHeight - 8, 14 + measured, height - 8));
        canvas.DrawText(caption, 14, height - 16, SKTextAlign.Left, font, textPaint);
        canvas.Restore();
    }

    private static SKRect ScaleAroundCenter(SKRect rect, float scale)
    {
        var halfWidth = rect.Width * scale / 2;
        var halfHeight = rect.Height * scale / 2;
        return new SKRect(rect.MidX - halfWidth, rect.MidY - halfHeight, rect.MidX + halfWidth, rect.MidY + halfHeight);
    }

    private static void DrawBitmapAlpha(SKCanvas canvas, SKBitmap bitmap, SKRect destination, byte alpha)
    {
        using var paint = new SKPaint { Color = new SKColor(255, 255, 255, alpha), IsAntialias = true };
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        canvas.DrawBitmap(bitmap, destination, sampling, paint);
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
                DrawAutoText(canvas, workspace, w, string.IsNullOrWhiteSpace(w.RuntimeText) ? w.Label : w.RuntimeText!);
                break;
            case WidgetType.Value:
                DrawValue(canvas, workspace, w);
                break;
            case WidgetType.CircularGauge:
                DrawCircularGauge(canvas, workspace, w);
                break;
            case WidgetType.AnalogClock:
                DrawAnalogClock(canvas, workspace, w);
                break;
            case WidgetType.BarGauge:
                DrawBarGauge(canvas, workspace, w);
                break;
            case WidgetType.Graph:
                DrawGraph(canvas, workspace, w);
                break;
            case WidgetType.Image:
                DrawImage(canvas, workspace, w);
                break;
            case WidgetType.AnimatedImage:
                DrawAnimatedImage(canvas, workspace, w);
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

    private static void DrawAutoText(SKCanvas canvas, ThemeWorkspace workspace, WidgetModel w, string text)
    {
        FillBackground(canvas, w);
        var size = (float)Math.Max(6, w.FontSize);
        DrawCenteredText(canvas, workspace, w, text, size, w.ForegroundColor, (float)(w.Height / 2));
    }

    private static void DrawValue(SKCanvas canvas, ThemeWorkspace workspace, WidgetModel w)
    {
        FillBackground(canvas, w);
        var value = FormatValue(w);
        var padding = 4f;
        var availableWidth = (float)Math.Max(1, w.Width - padding * 2);

        if (w.ShowLabel && w.ShowValue)
        {
            var labelArea = (float)Math.Max(8, w.Height * .28);
            var valueArea = (float)Math.Max(8, w.Height - labelArea - padding * 2);
            var labelSize = (float)Math.Max(6, w.FontSize * .5);
            var valueSize = (float)Math.Max(6, w.FontSize);

            DrawCenteredText(canvas, workspace, w, w.Label, labelSize, w.ForegroundColor, labelArea * .55f);
            DrawCenteredText(canvas, workspace, w, value, valueSize, w.AccentColor, labelArea + valueArea * .50f);
        }
        else if (w.ShowLabel)
        {
            var labelSize = (float)Math.Max(6, w.FontSize);
            DrawCenteredText(canvas, workspace, w, w.Label, labelSize, w.ForegroundColor, (float)(w.Height / 2));
        }
        else if (w.ShowValue)
        {
            var valueSize = (float)Math.Max(6, w.FontSize);
            DrawCenteredText(canvas, workspace, w, value, valueSize, w.AccentColor, (float)(w.Height / 2));
        }
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

        var innerWidth = (float)Math.Max(8, diameter * .72);
        if (w.ShowValue)
        {
            var value = FormatValue(w);
            var valueSize = (float)Math.Max(6, w.FontSize);
            DrawCenteredText(canvas, workspace, w, value, valueSize, w.ForegroundColor, (float)(top + diameter * .53));
        }
        if (w.ShowLabel)
        {
            var labelSize = (float)Math.Max(6, w.FontSize * .55);
            DrawCenteredText(canvas, workspace, w, w.Label, labelSize, w.ForegroundColor, (float)(top + diameter * .72));
        }
    }

    private static void DrawAnalogClock(SKCanvas canvas, ThemeWorkspace workspace, WidgetModel w)
    {
        FillBackground(canvas, w);

        var padding = Math.Max(5f, (float)w.LineThickness + 3f);
        var diameter = (float)Math.Max(12, Math.Min(w.Width, w.Height) - padding * 2);
        var radius = diameter / 2f;
        var cx = (float)w.Width / 2f;
        var cy = (float)w.Height / 2f;

        using var face = Paint(w.SecondaryColor, Math.Max(1.5f, (float)w.LineThickness), SKPaintStyle.Stroke);
        canvas.DrawCircle(cx, cy, radius, face);

        using var minorTick = Paint(WithAlpha(ParseColor(w.ForegroundColor), 130), Math.Max(1f, diameter * .006f), SKPaintStyle.Stroke);
        using var majorTick = Paint(w.ForegroundColor, Math.Max(1.5f, diameter * .012f), SKPaintStyle.Stroke);

        for (var i = 0; i < 60; i++)
        {
            var angle = (float)(i * Math.PI * 2 / 60d - Math.PI / 2d);
            var major = i % 5 == 0;
            var outer = radius - Math.Max(2f, diameter * .035f);
            var inner = outer - (major ? diameter * .085f : diameter * .035f);

            var x1 = cx + MathF.Cos(angle) * inner;
            var y1 = cy + MathF.Sin(angle) * inner;
            var x2 = cx + MathF.Cos(angle) * outer;
            var y2 = cy + MathF.Sin(angle) * outer;
            canvas.DrawLine(x1, y1, x2, y2, major ? majorTick : minorTick);
        }

        var now = DateTime.Now;
        var hourAngle = (float)(((now.Hour % 12) + now.Minute / 60d + now.Second / 3600d) * 30d - 90d);
        var minuteAngle = (float)((now.Minute + now.Second / 60d) * 6d - 90d);
        var secondAngle = (float)(now.Second * 6d - 90d);

        static SKPoint End(float centerX, float centerY, float length, float degrees)
        {
            var radians = degrees * MathF.PI / 180f;
            return new SKPoint(
                centerX + MathF.Cos(radians) * length,
                centerY + MathF.Sin(radians) * length);
        }

        var hourEnd = End(cx, cy, radius * .50f, hourAngle);
        var minuteEnd = End(cx, cy, radius * .72f, minuteAngle);
        var secondEnd = End(cx, cy, radius * .80f, secondAngle);

        using var hourPaint = Paint(w.ForegroundColor, Math.Max(3f, diameter * .035f), SKPaintStyle.Stroke);
        hourPaint.StrokeCap = SKStrokeCap.Round;
        using var minutePaint = Paint(w.ForegroundColor, Math.Max(2f, diameter * .022f), SKPaintStyle.Stroke);
        minutePaint.StrokeCap = SKStrokeCap.Round;
        using var secondPaint = Paint(w.AccentColor, Math.Max(1f, diameter * .010f), SKPaintStyle.Stroke);
        secondPaint.StrokeCap = SKStrokeCap.Round;

        canvas.DrawLine(cx, cy, hourEnd.X, hourEnd.Y, hourPaint);
        canvas.DrawLine(cx, cy, minuteEnd.X, minuteEnd.Y, minutePaint);
        canvas.DrawLine(cx, cy, secondEnd.X, secondEnd.Y, secondPaint);

        using var centerPaint = Paint(w.AccentColor, style: SKPaintStyle.Fill);
        canvas.DrawCircle(cx, cy, Math.Max(2.5f, diameter * .025f), centerPaint);
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

        var bitmap = RenderResourceCache.GetImage(path);
        if (bitmap is null) return;
        var dest = new SKRect(0, 0, (float)w.Width, (float)w.Height);
        var src = SourceRectForFit(bitmap.Width, bitmap.Height, dest.Width, dest.Height, w.MediaFit);
        canvas.DrawBitmap(bitmap, src, dest);
    }

    private static void DrawAnimatedImage(SKCanvas canvas, ThemeWorkspace workspace, WidgetModel w)
    {
        FillBackground(canvas, w);

        var asset = workspace.Document.Assets.FirstOrDefault(a => a.Id == w.AssetId);
        var path = asset is null ? null : workspace.GetAbsolutePath(asset);
        if (path is null || !File.Exists(path))
        {
            DrawCenteredText(canvas, workspace, w, "GIF", 12, w.ForegroundColor, (float)(w.Height / 2));
            return;
        }

        var bitmap = AnimatedGifFrameProvider.GetFrame(
            path,
            w.PlaybackSpeed,
            w.TargetFps,
            w.Loop);

        if (bitmap is null)
        {
            DrawCenteredText(canvas, workspace, w, "GIF", 12, w.ForegroundColor, (float)(w.Height / 2));
            return;
        }

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

    private static float FitTextSize(ThemeWorkspace workspace, WidgetModel w, string text, float maxWidth, float maxHeight, float maxSize)
    {
        if (string.IsNullOrEmpty(text))
            return 6;

        maxWidth = Math.Max(1, maxWidth);
        maxHeight = Math.Max(1, maxHeight);
        var low = 4f;
        var high = Math.Clamp(maxSize, 6f, 300f);

        var typeface = Typeface(workspace, w);
        using var paint = new SKPaint { IsAntialias = true };

        for (var i = 0; i < 12; i++)
        {
            var mid = (low + high) / 2f;
            using var font = new SKFont(typeface, mid);
            var width = font.MeasureText(text, paint);
            var metrics = font.Metrics;
            var height = metrics.Descent - metrics.Ascent;

            if (width <= maxWidth && height <= maxHeight)
                low = mid;
            else
                high = mid;
        }

        return Math.Max(4, (float)Math.Floor(low));
    }

    private static void DrawText(SKCanvas canvas, ThemeWorkspace workspace, WidgetModel w, string text, bool verticalCenter = false, float? y = null, float? size = null, string? color = null)
    {
        var typeface = Typeface(workspace, w);
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
        var typeface = Typeface(workspace, w);
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
        var typeface = Typeface(workspace, w);
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
                    return RenderResourceCache.GetTypefaceFromFile(path);
            }
        }
        if (BuiltInFontCatalog.TryGetPath(w.FontFamily, out var builtInPath))
            return RenderResourceCache.GetTypefaceFromFile(builtInPath);

        var style = w.FontBold && w.FontItalic ? SKFontStyle.BoldItalic : w.FontBold ? SKFontStyle.Bold : w.FontItalic ? SKFontStyle.Italic : SKFontStyle.Normal;
        return RenderResourceCache.GetTypefaceFromFamily(w.FontFamily, style);
    }

    private static string FormatValue(WidgetModel w)
    {
        if (!string.IsNullOrWhiteSpace(w.RuntimeText))
            return w.RuntimeText!;
        var value = w.DisplayValue.ToString(string.IsNullOrWhiteSpace(w.ValueFormat) ? "0" : w.ValueFormat, CultureInfo.CurrentCulture);
        return value + (w.RuntimeUnit ?? w.Suffix);
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
