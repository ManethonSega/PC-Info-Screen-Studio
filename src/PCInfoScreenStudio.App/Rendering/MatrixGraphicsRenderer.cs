using PCInfoScreenStudio.Models;
using SkiaSharp;

namespace PCInfoScreenStudio.Rendering;

/// <summary>Native LED primitives, with no desktop scaling or firmware effects.</summary>
public static class MatrixGraphicsRenderer
{
    public static int FlightOrigin(MatrixWidgetSettings w,long step)
    {
        int cycle=w.Width+w.ArtWidth;
        int phase=w.AnimationPlaying ? (int)((Math.Max(0,step)%(cycle*5L))*w.FlightSpeed/5%cycle) : 0;
        int origin=w.FlightDirection==MatrixFlightDirection.RightToLeft ? w.Width-w.ArtWidth-phase : phase;
        if(origin < -w.ArtWidth)origin+=cycle;
        if(origin>=w.Width)origin-=cycle;
        return origin;
    }
    public static byte[] Render(MatrixWidgetSettings w, MatrixWidgetReading reading, long step)
    {
        var pixels = new byte[2700];
        void Put(int x, int y, SKColor c)
        {
            if (x < 0 || x >= w.Width || y < 0 || y >= w.Height) return;
            int p = (y * 90 + x) * 3; pixels[p] = c.Red; pixels[p+1] = c.Green; pixels[p+2] = c.Blue;
        }
        if (w.Content == MatrixContent.FlyingArt)
        {
            int origin=FlightOrigin(w,step);
            for (int y = 0; y < w.Height; y++) for (int x = 0; x < w.ArtWidth; x++)
            {
                int sx = w.FlightDirection == MatrixFlightDirection.RightToLeft ? w.ArtWidth - 1 - x : x;
                int p = (y * 90 + sx) * 3;
                if (w.Pixels[p] != 0 || w.Pixels[p+1] != 0 || w.Pixels[p+2] != 0)
                    Put(origin + x,y,new(w.Pixels[p],w.Pixels[p+1],w.Pixels[p+2]));
            }
            return pixels;
        }
        var preset = MatrixBarPresets.Get(w.BarStyle);
        double? value = w.UseSensor ? reading.Numeric : w.ManualValue;
        double fraction = value is double v && double.IsFinite(v) && w.RangeMaximum > w.RangeMinimum
            ? Math.Clamp((v - w.RangeMinimum) / (w.RangeMaximum - w.RangeMinimum),0,1) : 0;
        SKColor fill = w.Alarm && value >= w.AlarmThreshold ? SKColors.Red : SKColor.Parse(w.Colour);
        var dark = new SKColor(17,29,42); var edge = new SKColor(153,183,203);
        var light = new SKColor((byte)Math.Min(255,fill.Red+45),(byte)Math.Min(255,fill.Green+45),(byte)Math.Min(255,fill.Blue+45));
        if (preset.Pattern == MatrixBarPattern.Hearts)
        {
            var heart = MatrixArtBank.Get("heart",w.Height == 4 ? 4 : 5);
            int count = Math.Max(1,w.Width / (heart.Width+1));
            for (int i=0;i<count;i++) for (int y=0;y<heart.Height;y++) for (int x=0;x<heart.Width;x++)
            {
                int p=(y*90+x)*3;
                if (heart.Pixels[p] != 0 || heart.Pixels[p+1] != 0 || heart.Pixels[p+2] != 0)
                    Put(i*(heart.Width+1)+x,(w.Height-heart.Height)/2+y,i < (int)Math.Ceiling(fraction*count) ? fill : edge);
            }
            return pixels;
        }
        int start = 0;
        if (preset.Badge is not null && w.Width >= w.Height+8)
        {
            if (preset.Badge == "shield")
            {
                for (int y=0;y<w.Height;y++) for (int x=0;x<w.Height;x++)
                {
                    int taper=Math.Max(0,y-w.Height/2);
                    if(x>=taper && x<w.Height-taper) Put(x,y,x==w.Height/2 || y==w.Height/2 ? edge : fill);
                }
                start=w.Height+1;
            }
            else
            {
                var badge=MatrixArtBank.Get(preset.Badge,w.Height);
                for(int y=0;y<w.Height;y++) for(int x=0;x<badge.Width;x++)
                {
                    int p=(y*90+x)*3;
                    if(badge.Pixels[p]!=0 || badge.Pixels[p+1]!=0 || badge.Pixels[p+2]!=0)
                        Put(x,y,new(badge.Pixels[p],badge.Pixels[p+1],badge.Pixels[p+2]));
                }
                start=badge.Width+1;
            }
        }
        int end=w.Width-1, inside=Math.Max(0,end-start-1);
        int segmentCount=Math.Max(1,Math.Min(preset.Pattern == MatrixBarPattern.Ticks ? 12 : 5,(inside+1)/2));
        for(int y=0;y<w.Height;y++) for(int x=start;x<=end;x++)
        {
            bool side=x==start || x==end, top=y==0 || y==w.Height-1;
            if(top && side && w.Width-start>3) continue;
            if(top || side) { Put(x,y,edge); continue; }
            int u=x-start-1;
            bool on=u < (int)Math.Ceiling(fraction*inside);
            bool gap=false;
            if(preset.Pattern is MatrixBarPattern.Segments or MatrixBarPattern.Ticks)
            {
                int segment=u*segmentCount/Math.Max(1,inside);
                gap=u>0 && segment != (u-1)*segmentCount/Math.Max(1,inside);
                on=segment < (int)Math.Ceiling(fraction*segmentCount);
            }
            else if(preset.Pattern == MatrixBarPattern.Chevrons) gap=(u-Math.Abs(y-(w.Height-1)/2)+100)%5 >= 3;
            else if(preset.Pattern == MatrixBarPattern.Diagonal) gap=(u+y)%5 == 0;
            Put(x,y,gap || !on ? dark : y==1 ? light : fill);
        }
        return pixels;
    }
}
