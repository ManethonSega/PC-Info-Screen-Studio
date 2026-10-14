using System.Globalization;
using System.Text;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace PCInfoScreenStudio.Rendering;

/// <summary>Preserves the selected text font while shaping whole emoji graphemes in a colour font.</summary>
public static class ColoredTextRenderer
{
    private const int LayoutLimit = 128;
    private static readonly object Sync = new();
    private static readonly Dictionary<(SKTypeface Face, float Size, string Text), Layout> Layouts = [];
    private static readonly Queue<(SKTypeface Face, float Size, string Text)> Order = [];

    public readonly record struct TextMetrics(float Ascent, float Descent, float Top, float Bottom);

    public static float Measure(string? text, SKFont font, SKPaint paint)
    {
        text ??= string.Empty;
        if (!ContainsEmoji(text)) return font.MeasureText(text, paint);
        lock (Sync)
        {
            var layout = GetLayout(text, font, out var temporary);
            try { return layout.Width; }
            finally { if (temporary) layout.Dispose(); }
        }
    }

    public static TextMetrics Metrics(string? text, SKFont font)
    {
        text ??= string.Empty;
        var metrics = font.Metrics;
        if (!ContainsEmoji(text)) return new(metrics.Ascent, metrics.Descent, metrics.Top, metrics.Bottom);
        lock (Sync)
        {
            var layout = GetLayout(text, font, out var temporary);
            try { return layout.Metrics; }
            finally { if (temporary) layout.Dispose(); }
        }
    }

    public static void Draw(SKCanvas canvas, string? text, float x, float baseline, SKFont font, SKPaint paint)
    {
        text ??= string.Empty;
        if (string.IsNullOrEmpty(text)) return;
        if (!ContainsEmoji(text)) { canvas.DrawText(text, x, baseline, SKTextAlign.Left, font, paint); return; }
        lock (Sync)
        {
            var layout = GetLayout(text, font, out var temporary);
            try
            {
                foreach (var run in layout.Runs)
                {
                    // Colour glyphs use their own palette. Outline passes apply to ordinary text only.
                    if (run.Emoji && paint.Style == SKPaintStyle.Stroke) continue;
                    canvas.DrawText(run.Blob, x + run.Offset, baseline, paint);
                }
            }
            finally { if (temporary) layout.Dispose(); }
        }
    }

    public static bool ContainsEmoji(string? text)
        => !string.IsNullOrEmpty(text) && text.EnumerateRunes().Any(r =>
            r.Value is >= 0x1F000 and <= 0x1FAFF or >= 0x2600 and <= 0x27BF or
                0x231A or 0x231B or >= 0x23E9 and <= 0x23EC or 0x23F0 or 0x23F3 or
                0x25FD or 0x25FE or 0x2B1B or 0x2B1C or 0x2B50 or 0x2B55 or 0xFE0F or 0x20E3);

    public static void Clear()
    {
        lock (Sync)
        {
            foreach (var layout in Layouts.Values) layout.Dispose();
            Layouts.Clear(); Order.Clear();
        }
    }

    private static Layout GetLayout(string text, SKFont font, out bool temporary)
    {
        // Large pasted strings are rendered without retaining a permanent layout.
        temporary = text.Length > 2048;
        var key = (font.Typeface, font.Size, text);
        if (!temporary && Layouts.TryGetValue(key, out var cached)) return cached;
        var layout = BuildLayout(text, font);
        if (temporary) return layout;
        while (Layouts.Count >= LayoutLimit)
            if (Layouts.Remove(Order.Dequeue(), out var evicted)) evicted.Dispose();
        Layouts.Add(key, layout); Order.Enqueue(key);
        return layout;
    }

    private static Layout BuildLayout(string text, SKFont font)
    {
        var runs = new List<Run>();
        var metrics = font.Metrics;
        var ascent = metrics.Ascent; var descent = metrics.Descent;
        var top = metrics.Top; var bottom = metrics.Bottom;
        var offset = 0f;
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        var current = new StringBuilder();
        var currentEmoji = false;
        var hasCurrent = false;
        try
        {
            while (enumerator.MoveNext())
            {
                var element = enumerator.GetTextElement();
                // VS15 explicitly requests monochrome text presentation.
                var emoji = ContainsEmoji(element) && !element.Contains('\uFE0E');
                if (hasCurrent && emoji != currentEmoji) Flush();
                currentEmoji = emoji; hasCurrent = true; current.Append(element);
            }
            Flush();
            return new Layout(runs, offset, new(ascent, descent, top, bottom));
        }
        catch { foreach (var run in runs) run.Dispose(); throw; }

        void Flush()
        {
            if (current.Length == 0) return;
            var value = current.ToString(); current.Clear();
            var typeface = currentEmoji
                ? RenderResourceCache.GetTypefaceFromFamily("Segoe UI Emoji", SKFontStyle.Normal)
                : font.Typeface;
            using var runFont = new SKFont(typeface, font.Size);
            using var shaper = new SKShaper(typeface);
            var result = shaper.Shape(value, runFont);
            if (result.Codepoints.Length == 0) return;
            using var builder = new SKTextBlobBuilder();
            var buffer = builder.AllocateRawPositionedRun(runFont, result.Codepoints.Length, null);
            for (var i = 0; i < result.Codepoints.Length; i++)
            {
                buffer.Glyphs[i] = (ushort)result.Codepoints[i];
                buffer.Positions[i] = result.Points[i];
            }
            var blob = builder.Build() ?? throw new InvalidOperationException("Could not build the shaped text run.");
            runs.Add(new Run(blob, offset, currentEmoji));
            var bounds = blob.Bounds;
            ascent = Math.Min(ascent, bounds.Top); descent = Math.Max(descent, bounds.Bottom);
            top = Math.Min(top, bounds.Top); bottom = Math.Max(bottom, bounds.Bottom);
            offset += result.Width;
        }
    }

    private sealed record Run(SKTextBlob Blob, float Offset, bool Emoji) : IDisposable
    { public void Dispose() => Blob.Dispose(); }
    private sealed record Layout(List<Run> Runs, float Width, TextMetrics Metrics) : IDisposable
    { public void Dispose() { foreach (var run in Runs) run.Dispose(); } }
}
