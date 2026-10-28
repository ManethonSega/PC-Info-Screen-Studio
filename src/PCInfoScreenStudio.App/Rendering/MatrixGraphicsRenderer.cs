using PCInfoScreenStudio.Models;
using SkiaSharp;

namespace PCInfoScreenStudio.Rendering;

/// <summary>Native LED primitives, with no desktop scaling or firmware effects.</summary>
public static class MatrixGraphicsRenderer
{
    public static int MotionOrigin(int width, int contentWidth, MatrixFlightDirection direction, int speed, bool playing, long step)
    {
        int cycle = Math.Max(1, width + contentWidth);
        int phase = playing ? (int)((Math.Max(0, step) % (cycle * 5L)) * Math.Clamp(speed, 1, 20) / 5 % cycle) : 0;
        int origin = direction == MatrixFlightDirection.RightToLeft ? width - contentWidth - phase : phase;
        if (origin < -contentWidth) origin += cycle;
        if (origin >= width) origin -= cycle;
        return origin;
    }

    public static int FlightOrigin(MatrixWidgetSettings w, long step)
        => MotionOrigin(w.Width, w.ArtWidth, w.FlightDirection, w.FlightSpeed, w.AnimationPlaying, step);

    public static byte[] Render(MatrixWidgetSettings w, MatrixWidgetReading reading, long step)
    {
        var pixels = new byte[2700];
        void Put(int x, int y, SKColor c)
        {
            if (x < 0 || x >= w.Width || y < 0 || y >= w.Height) return;
            int p = (y * 90 + x) * 3;
            pixels[p] = c.Red; pixels[p + 1] = c.Green; pixels[p + 2] = c.Blue;
        }

        if (w.Content == MatrixContent.FlyingArt)
        {
            RenderAnimation(w, step, Put);
            return pixels;
        }

        RenderBar(w, reading, Put);
        return pixels;
    }

    private static void RenderAnimation(MatrixWidgetSettings w, long step, Action<int, int, SKColor> put)
    {
        if (w.AnimationStyle == MatrixAnimationStyle.Rocket)
        {
            RenderRocket(w, step, put);
            return;
        }

        SKColor primary = SKColor.Parse(w.Colour);
        int shift = w.AnimationPlaying ? (int)(Math.Max(0, step) * w.FlightSpeed / 5) : 0;
        switch (w.AnimationStyle)
        {
            case MatrixAnimationStyle.Crab:
            case MatrixAnimationStyle.Cat:
                RenderCreature(w, step, primary, put);
                break;
            case MatrixAnimationStyle.Starfield:
                RenderStarfield(w, step, primary, put);
                break;
            case MatrixAnimationStyle.Rainbow:
                for (int y = 0; y < w.Height; y++) for (int x = 0; x < w.Width; x++)
                    put(x, y, SKColor.FromHsv((x * 360f / Math.Max(1, w.Width) + shift * 12) % 360, 92, 100));
                break;
            case MatrixAnimationStyle.Breathing:
                double pulse = w.AnimationPlaying ? .2 + .8 * (.5 + .5 * Math.Sin(step * w.FlightSpeed * .16)) : 1;
                SKColor breath = Scale(primary, pulse);
                for (int y = 0; y < w.Height; y++) for (int x = 0; x < w.Width; x++) put(x, y, breath);
                break;
            case MatrixAnimationStyle.Wave:
                for (int x = 0; x < w.Width; x++)
                {
                    int travel = w.FlightDirection == MatrixFlightDirection.RightToLeft ? shift : -shift;
                    int centre = (int)Math.Round((w.Height - 1) / 2d + Math.Sin((x + travel) * Math.PI / Math.Max(3, w.Height)) * Math.Max(1, (w.Height - 2) / 2d));
                    put(x, centre, primary);
                    if (w.Height >= 6) put(x, Math.Min(w.Height - 1, centre + 1), Scale(primary, .4));
                }
                break;
            case MatrixAnimationStyle.TwoTone:
                SKColor second = new((byte)(255 - primary.Red / 2), (byte)(255 - primary.Green / 2), (byte)(255 - primary.Blue / 2));
                for (int y = 0; y < w.Height; y++) for (int x = 0; x < w.Width; x++)
                {
                    int travel = w.FlightDirection == MatrixFlightDirection.RightToLeft ? shift : -shift;
                    put(x, y, ((x + travel + 4096) / Math.Max(2, w.Height / 2)) % 2 == 0 ? primary : second);
                }
                break;
            case MatrixAnimationStyle.Fade:
                for (int y = 0; y < w.Height; y++) for (int x = 0; x < w.Width; x++)
                {
                    int travel = w.FlightDirection == MatrixFlightDirection.RightToLeft ? x + shift : x - shift;
                    double level = .12 + .88 * (1 - Math.Abs(((travel % 16 + 16) % 16) - 7.5) / 7.5);
                    put(x, y, Scale(primary, level));
                }
                break;
            case MatrixAnimationStyle.Gradient:
                for (int y = 0; y < w.Height; y++) for (int x = 0; x < w.Width; x++)
                {
                    int travel = w.FlightDirection == MatrixFlightDirection.RightToLeft ? x + shift : x - shift;
                    double mix = .5 + .5 * Math.Sin(travel * Math.PI / 15);
                    put(x, y, Blend(primary, new SKColor(240, 55, 203), mix));
                }
                break;
            case MatrixAnimationStyle.Meteor:
                RenderMeteor(w, step, primary, put);
                break;
        }
    }

    private static void RenderRocket(MatrixWidgetSettings w, long step, Action<int, int, SKColor> put)
    {
        int origin = FlightOrigin(w, step);
        for (int y = 0; y < w.Height; y++) for (int x = 0; x < w.ArtWidth; x++)
        {
            int sx = w.FlightDirection == MatrixFlightDirection.RightToLeft ? w.ArtWidth - 1 - x : x;
            int p = (y * 90 + sx) * 3;
            if (w.Pixels[p] != 0 || w.Pixels[p + 1] != 0 || w.Pixels[p + 2] != 0)
                put(origin + x, y, new(w.Pixels[p], w.Pixels[p + 1], w.Pixels[p + 2]));
        }

        if (!w.AnimationPlaying) return;
        int[] flameLengths = [1, 3, 0, 2, 4, 1];
        int length = flameLengths[(int)(Math.Max(0, step) % flameLengths.Length)];
        int tail = w.FlightDirection == MatrixFlightDirection.RightToLeft ? origin + w.ArtWidth : origin - 1;
        int trail = w.FlightDirection == MatrixFlightDirection.RightToLeft ? 1 : -1;
        int centre = w.Height / 2;
        for (int i = 0; i < length; i++)
        {
            SKColor colour = i == 0 ? new(255, 220, 40) : i == 1 ? new(255, 125, 15) : new(190, 48, 20);
            put(tail + trail * i, centre, colour);
            if (w.Height >= 6 && i < 2) put(tail + trail * i, centre - 1 + (int)(step + i) % 2, Scale(colour, .72));
        }
    }

    private static void RenderCreature(MatrixWidgetSettings w, long step, SKColor colour, Action<int, int, SKColor> put)
    {
        bool secondFrame = w.AnimationPlaying && (step / 2) % 2 != 0;
        string[] source = w.AnimationStyle == MatrixAnimationStyle.Crab
            ? secondFrame
                ? ["R...R...R", ".R.ERE.R.", "..RRRRR..", "R.R.R.R.R", ".R.....R."]
                : ["R...R...R", ".R.ERE.R.", "..RRRRR..", ".R.R.R.R.", "R.......R"]
            : secondFrame
                ? ["P.P.....", "PPPP....", "PEEP..P.", "PPPPPPP.", "P.P.P..P"]
                : ["P.P.....", "PPPP....", "PEEP...P", "PPPPPPPP", ".P.P.P.."];
        int sourceWidth = source.Max(r => r.Length);
        int spriteWidth = Math.Max(3, (int)Math.Round(sourceWidth * w.Height / (double)source.Length));
        int origin = MotionOrigin(w.Width, spriteWidth, w.FlightDirection, w.FlightSpeed, w.AnimationPlaying, step);
        for (int y = 0; y < w.Height; y++) for (int x = 0; x < spriteWidth; x++)
        {
            int sy = Math.Min(source.Length - 1, y * source.Length / w.Height);
            int sx = Math.Min(sourceWidth - 1, x * sourceWidth / spriteWidth);
            if (w.FlightDirection == MatrixFlightDirection.RightToLeft) sx = sourceWidth - 1 - sx;
            char pixel = sx < source[sy].Length ? source[sy][sx] : '.';
            if (pixel == 'E') put(origin + x, y, SKColors.White);
            else if (pixel != '.') put(origin + x, y, colour);
        }
    }

    private static void RenderStarfield(MatrixWidgetSettings w, long step, SKColor colour, Action<int, int, SKColor> put)
    {
        int phase = w.AnimationPlaying ? (int)(step % 5) : 0;
        for (int y = 0; y < w.Height; y++) for (int x = 0; x < w.Width; x++)
        {
            int hash = (x * 37 + y * 61 + 17) % 23;
            if (hash % 5 == phase) put(x, y, hash % 2 == 0 ? colour : Scale(colour, .35));
        }
        if (!w.AnimationPlaying) return;
        int head = MotionOrigin(w.Width, 1, w.FlightDirection, w.FlightSpeed, true, step);
        int trail = w.FlightDirection == MatrixFlightDirection.RightToLeft ? 1 : -1;
        int row = (int)((step / 9) % Math.Max(1, w.Height));
        for (int i = 0; i < Math.Min(6, w.Width); i++) put(head + trail * i, row, Scale(SKColors.White, 1 - i * .13));
    }

    private static void RenderMeteor(MatrixWidgetSettings w, long step, SKColor colour, Action<int, int, SKColor> put)
    {
        int head = MotionOrigin(w.Width, 1, w.FlightDirection, w.FlightSpeed, w.AnimationPlaying, step);
        int trail = w.FlightDirection == MatrixFlightDirection.RightToLeft ? 1 : -1;
        int length = Math.Min(w.Width, Math.Max(5, w.Width / 5));
        for (int i = 0; i < length; i++)
        {
            double level = 1 - i / (double)length;
            for (int y = 0; y < w.Height; y++) put(head + trail * i, y, Scale(colour, level * level));
        }
    }

    private static void RenderBar(MatrixWidgetSettings w, MatrixWidgetReading reading, Action<int, int, SKColor> put)
    {
        var preset = MatrixBarPresets.Get(w.BarStyle);
        double? value = w.UseSensor ? reading.Numeric : w.ManualValue;
        double fraction = value is double v && double.IsFinite(v) && w.RangeMaximum > w.RangeMinimum
            ? Math.Clamp((v - w.RangeMinimum) / (w.RangeMaximum - w.RangeMinimum), 0, 1) : 0;
        SKColor fill = w.Alarm && value >= w.AlarmThreshold ? SKColors.Red : SKColor.Parse(w.Colour);
        var dark = new SKColor(17, 29, 42); var edge = new SKColor(153, 183, 203);
        var light = new SKColor((byte)Math.Min(255, fill.Red + 45), (byte)Math.Min(255, fill.Green + 45), (byte)Math.Min(255, fill.Blue + 45));
        int start = 0;
        if (!string.IsNullOrWhiteSpace(w.Prefix))
        {
            var label = MatrixLayerRenderer.RenderLabel(w, w.Prefix.TrimEnd(), fill);
            int labelWidth = Math.Min(label.Width, Math.Max(0, w.Width - 4));
            for (int y = 0; y < w.Height; y++) for (int x = 0; x < labelWidth; x++)
            {
                int p = (y * label.Width + x) * 4;
                if (label.Rgba[p + 3] != 0) put(x, y, new(label.Rgba[p], label.Rgba[p + 1], label.Rgba[p + 2]));
            }
            start = Math.Min(w.Width - 3, labelWidth + 1);
        }

        if (preset.Pattern == MatrixBarPattern.Hearts)
        {
            var heart = MatrixArtBank.Get("heart", w.Height == 10 ? 5 : w.Height);
            int count = Math.Max(1, (w.Width - start) / (heart.Width + 1));
            for (int i = 0; i < count; i++) for (int y = 0; y < heart.Height; y++) for (int x = 0; x < heart.Width; x++)
            {
                int p = (y * 90 + x) * 3;
                if (heart.Pixels[p] != 0 || heart.Pixels[p + 1] != 0 || heart.Pixels[p + 2] != 0)
                    put(start + i * (heart.Width + 1) + x, (w.Height - heart.Height) / 2 + y, i < (int)Math.Ceiling(fraction * count) ? fill : edge);
            }
            return;
        }
        if (preset.Badge is not null && w.Width - start >= w.Height + 8)
        {
            if (preset.Badge == "shield")
            {
                for (int y = 0; y < w.Height; y++) for (int x = 0; x < w.Height; x++)
                {
                    int taper = Math.Max(0, y - w.Height / 2);
                    if (x >= taper && x < w.Height - taper) put(start + x, y, x == w.Height / 2 || y == w.Height / 2 ? edge : fill);
                }
                start += w.Height + 1;
            }
            else
            {
                var badge = MatrixArtBank.Get(preset.Badge, w.Height);
                for (int y = 0; y < w.Height; y++) for (int x = 0; x < badge.Width; x++)
                {
                    int p = (y * 90 + x) * 3;
                    if (badge.Pixels[p] != 0 || badge.Pixels[p + 1] != 0 || badge.Pixels[p + 2] != 0)
                        put(start + x, y, new(badge.Pixels[p], badge.Pixels[p + 1], badge.Pixels[p + 2]));
                }
                start += badge.Width + 1;
            }
        }
        int end = w.Width - 1, inside = Math.Max(0, end - start - 1);
        int segmentCount = Math.Max(1, Math.Min(preset.Pattern == MatrixBarPattern.Ticks ? 12 : 5, (inside + 1) / 2));
        for (int y = 0; y < w.Height; y++) for (int x = start; x <= end; x++)
        {
            bool side = x == start || x == end, top = y == 0 || y == w.Height - 1;
            if (top && side && w.Width - start > 3) continue;
            if (top || side) { put(x, y, edge); continue; }
            int u = x - start - 1;
            bool on = u < (int)Math.Ceiling(fraction * inside);
            bool gap = false;
            if (preset.Pattern is MatrixBarPattern.Segments or MatrixBarPattern.Ticks)
            {
                int segment = u * segmentCount / Math.Max(1, inside);
                gap = u > 0 && segment != (u - 1) * segmentCount / Math.Max(1, inside);
                on = segment < (int)Math.Ceiling(fraction * segmentCount);
            }
            else if (preset.Pattern == MatrixBarPattern.Chevrons)
            {
                int point = (int)Math.Floor((w.Height - 3) / 2d - Math.Abs(y - (w.Height - 1) / 2d));
                int repeat = Math.Max(4, w.Height / 2 + 2);
                gap = (u - point + repeat) % repeat >= 2;
            }
            else if (preset.Pattern == MatrixBarPattern.Diagonal) gap = (u + y) % 5 == 0;
            put(x, y, gap || !on ? dark : y == 1 ? light : fill);
        }
    }

    private static SKColor Scale(SKColor colour, double level) => new(
        (byte)Math.Clamp((int)Math.Round(colour.Red * level), 0, 255),
        (byte)Math.Clamp((int)Math.Round(colour.Green * level), 0, 255),
        (byte)Math.Clamp((int)Math.Round(colour.Blue * level), 0, 255));

    private static SKColor Blend(SKColor a, SKColor b, double amount) => new(
        (byte)Math.Round(a.Red + (b.Red - a.Red) * amount),
        (byte)Math.Round(a.Green + (b.Green - a.Green) * amount),
        (byte)Math.Round(a.Blue + (b.Blue - a.Blue) * amount));
}
