using System.Text.Json;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Rendering;

internal static class MatrixGraphicsChecks
{
    public static void Check(string? previewPath = null)
    {
        var previews = new List<object>();
        var arrow=MatrixGraphicsRenderer.Render(new() { Content=MatrixContent.ProgressBar,BarStyle=MatrixBarStyle.Arrows,Colour="#F43CCC",Width=40,Height=10,UseSensor=false,ManualValue=100 },MatrixWidgetReading.Unavailable,0);
        int ArrowStart(int y)=>Enumerable.Range(1,38).First(x=>arrow[(y*90+x)*3]>200 && arrow[(y*90+x)*3+2]>100);
        Require(ArrowStart(4)>ArrowStart(1),"Arrow segments must point right, with their centre farther right than their top stroke.");
        foreach(var style in MatrixBarPresets.All) foreach(int h in new[] {4,5,10})
        {
            var w = new MatrixWidgetSettings { Content=MatrixContent.ProgressBar, BarStyle=style.Value, Colour=style.Colour, Width=40, Height=h, UseSensor=false };
            w.ManualValue=0; var empty=MatrixGraphicsRenderer.Render(w,MatrixWidgetReading.Unavailable,0);
            w.ManualValue=100; var full=MatrixGraphicsRenderer.Render(w,MatrixWidgetReading.Unavailable,0);
            Require(!full.SequenceEqual(empty),"Every native bar style changes its actual LEDs with progress at every height.");
            w.ManualValue=200; Require(full.SequenceEqual(MatrixGraphicsRenderer.Render(w,MatrixWidgetReading.Unavailable,0)),"High progress clamps at the range maximum.");
            w.ManualValue=-10; Require(empty.SequenceEqual(MatrixGraphicsRenderer.Render(w,MatrixWidgetReading.Unavailable,0)),"Low progress clamps at the range minimum.");
            w.ManualValue=60; var frame=MatrixGraphicsRenderer.Render(w,MatrixWidgetReading.Unavailable,0);
            Require(Enumerable.Range(0,900).All(i => i/90<h && i%90<40 || frame.Skip(i*3).Take(3).All(b=>b==0)),"Bars remain inside their configured LED rectangle.");
            previews.Add(new {style.Label,Height=h,Width=40,Pixels=Convert.ToBase64String(frame)});
        }
        foreach(int h in new[] {4,5,10})
        {
            var art=MatrixArtBank.Get("rocket",h);
            var w=new MatrixWidgetSettings { Content=MatrixContent.FlyingArt, ArtKey="rocket", ArtWidth=art.Width, Pixels=art.Pixels, Width=60, Height=h, FlightDirection=MatrixFlightDirection.LeftToRight, FlightSpeed=5 };
            var first=MatrixGraphicsRenderer.Render(w,MatrixWidgetReading.Unavailable,0);
            var moved=MatrixGraphicsRenderer.Render(w,MatrixWidgetReading.Unavailable,3);
            for(int y=0;y<h;y++) for(int x=0;x<art.Width;x++)
                Require(first.Skip((y*90+x)*3).Take(3).SequenceEqual(moved.Skip((y*90+x+3)*3).Take(3)),"Flight advances by the requested integer LED distance without changing artwork.");
            Require(first.SequenceEqual(MatrixGraphicsRenderer.Render(w,MatrixWidgetReading.Unavailable,60+art.Width)),"A complete flight loop returns to the identical frame.");
            w.FlightDirection=MatrixFlightDirection.RightToLeft;
            var reverse=MatrixGraphicsRenderer.Render(w,MatrixWidgetReading.Unavailable,0);
            for(int y=0;y<h;y++) for(int x=0;x<art.Width;x++)
                Require(first.Skip((y*90+x)*3).Take(3).SequenceEqual(reverse.Skip((y*90+59-x)*3).Take(3)),"Reversing flight mirrors the rocket so its nose points into its direction of travel.");
            w.AnimationPlaying=false;
            Require(reverse.SequenceEqual(MatrixGraphicsRenderer.Render(w,MatrixWidgetReading.Unavailable,30)),"Paused flight has a deterministic static frame.");
        }
        var state=new MatrixSettings { Widgets=[new() { Content=MatrixContent.ProgressBar, BarStyle=MatrixBarStyle.Coin, RangeMinimum=-50, RangeMaximum=150, UseSensor=false, ManualValue=75 },new() { Content=MatrixContent.FlyingArt, ArtKey="rocket", ArtWidth=20, FlightDirection=MatrixFlightDirection.LeftToRight,FlightSpeed=9 }] }.ValidatedCopy();
        var loaded=JsonSerializer.Deserialize<MatrixSettings>(JsonSerializer.Serialize(state))!.ValidatedCopy();
        Require(loaded.Widgets![0].Content==MatrixContent.ProgressBar && loaded.Widgets[0].BarStyle==MatrixBarStyle.Coin && loaded.Widgets[0].RangeMinimum==-50 && loaded.Widgets[1].ArtKey=="rocket" && loaded.Widgets[1].FlightSpeed==9 && loaded.Widgets[1].FlightDirection==MatrixFlightDirection.LeftToRight,"Graphic settings survive validation and restart without reverting to legacy text content.");
        if(previewPath is not null) System.IO.File.WriteAllText(previewPath,JsonSerializer.Serialize(previews));
        Console.WriteLine("PASS: 12 bar styles at all three heights, progress ranges, LED bounds, native rocket movement/mirroring/wrap/pause and graphic settings persistence.");
    }
    private static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
}
