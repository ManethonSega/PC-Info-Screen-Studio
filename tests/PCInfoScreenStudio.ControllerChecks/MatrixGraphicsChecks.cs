using System.Text.Json;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Rendering;

internal static class MatrixGraphicsChecks
{
    public static void Check(string? previewPath = null)
    {
        var previews = new List<object>();
        var arrow=MatrixGraphicsRenderer.Render(new() { Content=MatrixContent.ProgressBar,BarStyle=MatrixBarStyle.Arrows,Colour="#F43CCC",Width=45,Height=10,UseSensor=false,ManualValue=100 },MatrixWidgetReading.Unavailable,0);
        int ArrowStart(int y)=>Enumerable.Range(1,43).First(x=>arrow[(y*90+x)*3]>200 && arrow[(y*90+x)*3+2]>100);
        Require(ArrowStart(4)>ArrowStart(1),"Arrow segments must point right, with their centre farther right than their top stroke.");
        foreach(var style in MatrixBarPresets.All) foreach(int h in new[] {4,5,6,10}) foreach(int width in new[] {30,45,60})
        {
            var w = new MatrixWidgetSettings { Content=MatrixContent.ProgressBar, BarStyle=style.Value, Colour=style.Colour, Width=width, Height=h, UseSensor=false };
            w.ManualValue=0; var empty=MatrixGraphicsRenderer.Render(w,MatrixWidgetReading.Unavailable,0);
            w.ManualValue=100; var full=MatrixGraphicsRenderer.Render(w,MatrixWidgetReading.Unavailable,0);
            Require(!full.SequenceEqual(empty),"Every native bar style changes its actual LEDs with progress at every height.");
            w.ManualValue=200; Require(full.SequenceEqual(MatrixGraphicsRenderer.Render(w,MatrixWidgetReading.Unavailable,0)),"High progress clamps at the range maximum.");
            w.ManualValue=-10; Require(empty.SequenceEqual(MatrixGraphicsRenderer.Render(w,MatrixWidgetReading.Unavailable,0)),"Low progress clamps at the range minimum.");
            w.ManualValue=60; var frame=MatrixGraphicsRenderer.Render(w,MatrixWidgetReading.Unavailable,0);
            Require(Enumerable.Range(0,900).All(i => i/90<h && i%90<width || frame.Skip(i*3).Take(3).All(b=>b==0)),"Bars remain inside their configured LED rectangle.");
            previews.Add(new {style.Label,Height=h,Width=width,Pixels=Convert.ToBase64String(frame)});
        }
        var unlabelled=MatrixGraphicsRenderer.Render(new() { Content=MatrixContent.ProgressBar,Width=60,Height=6,UseSensor=false,ManualValue=65,Colour="#21E6E8" },MatrixWidgetReading.Unavailable,0);
        var labelled=MatrixGraphicsRenderer.Render(new() { Content=MatrixContent.ProgressBar,Width=60,Height=6,Prefix="CPU",UseSensor=false,ManualValue=65,Colour="#21E6E8" },MatrixWidgetReading.Unavailable,0);
        Require(!unlabelled.SequenceEqual(labelled) && Enumerable.Range(0,12).Any(x=>Enumerable.Range(0,6).Any(y=>labelled[(y*90+x)*3]!=0)),"A progress-bar label is rendered before the bar inside its selected width.");
        foreach(int h in new[] {4,5,6,10})
        {
            var art=MatrixArtBank.Get("rocket",h);
            var w=new MatrixWidgetSettings { Content=MatrixContent.FlyingArt, ArtKey="rocket", ArtWidth=art.Width, Pixels=art.Pixels, Width=60, Height=h, FlightDirection=MatrixFlightDirection.LeftToRight, FlightSpeed=5 };
            var first=MatrixGraphicsRenderer.Render(w,MatrixWidgetReading.Unavailable,0);
            var moved=MatrixGraphicsRenderer.Render(w,MatrixWidgetReading.Unavailable,3);
            for(int y=0;y<h;y++) for(int x=0;x<art.Width;x++)
                Require(first.Skip((y*90+x)*3).Take(3).SequenceEqual(moved.Skip((y*90+x+3)*3).Take(3)),"Flight advances by the requested integer LED distance without changing artwork.");
            long loop=LeastCommonMultiple(60+art.Width,6);
            Require(first.SequenceEqual(MatrixGraphicsRenderer.Render(w,MatrixWidgetReading.Unavailable,loop)),"A complete flight and flame loop returns to the identical frame.");
            w.FlightDirection=MatrixFlightDirection.RightToLeft;
            var reverse=MatrixGraphicsRenderer.Render(w,MatrixWidgetReading.Unavailable,0);
            for(int y=0;y<h;y++) for(int x=0;x<art.Width;x++)
                Require(first.Skip((y*90+x)*3).Take(3).SequenceEqual(reverse.Skip((y*90+59-x)*3).Take(3)),"Reversing flight mirrors the rocket so its nose points into its direction of travel.");
            w.AnimationPlaying=false;
            Require(reverse.SequenceEqual(MatrixGraphicsRenderer.Render(w,MatrixWidgetReading.Unavailable,30)),"Paused flight has a deterministic static frame.");
        }
        foreach(var animation in MatrixAnimationPresets.All) foreach(int height in new[] {4,5,6,10}) foreach(int area in new[] {60,90})
        {
            var w=new MatrixWidgetSettings { Content=MatrixContent.FlyingArt,AnimationStyle=animation.Value,Name=animation.Label,Colour=animation.Colour,Width=area,Height=height,FlightSpeed=7,FlightDirection=MatrixFlightDirection.LeftToRight };
            if(animation.Value==MatrixAnimationStyle.Rocket) { var art=MatrixArtBank.Get("rocket",height);w.ArtKey="rocket";w.ArtWidth=art.Width;w.Pixels=art.Pixels; }
            var first=MatrixGraphicsRenderer.Render(w,MatrixWidgetReading.Unavailable,0);
            var moved=Enumerable.Range(1,20).Select(i=>MatrixGraphicsRenderer.Render(w,MatrixWidgetReading.Unavailable,i)).FirstOrDefault(frame=>!first.SequenceEqual(frame)) ?? first;
            Require(first.Any(b=>b!=0) && moved.Any(b=>b!=0) && !first.SequenceEqual(moved),$"{animation.Label} must produce changing native LED frames at {height}×{area}.");
            Require(Enumerable.Range(0,900).All(i=>i/90<height && i%90<area || moved.Skip(i*3).Take(3).All(b=>b==0)),"Animations remain clipped to their 60- or 90-dot area.");
            w.AnimationPlaying=false;
            Require(MatrixGraphicsRenderer.Render(w,MatrixWidgetReading.Unavailable,2).SequenceEqual(MatrixGraphicsRenderer.Render(w,MatrixWidgetReading.Unavailable,30)),"Every animation has a deterministic stopped frame.");
        }
        var state=new MatrixSettings { Widgets=[new() { Content=MatrixContent.ProgressBar, Prefix="CPU",Width=44,Height=6,BarStyle=MatrixBarStyle.Coin,RangeMinimum=-50,RangeMaximum=150,UseSensor=false,ManualValue=75 },new() { Content=MatrixContent.FlyingArt,AnimationStyle=MatrixAnimationStyle.Cat,Width=61,ArtKey="rocket",ArtWidth=20,FlightDirection=MatrixFlightDirection.LeftToRight,FlightSpeed=9 }] }.ValidatedCopy();
        var loaded=JsonSerializer.Deserialize<MatrixSettings>(JsonSerializer.Serialize(state))!.ValidatedCopy();
        Require(loaded.Widgets![0].Content==MatrixContent.ProgressBar && loaded.Widgets[0].Prefix=="CPU" && loaded.Widgets[0].Width==45 && loaded.Widgets[0].Height==6 && loaded.Widgets[0].BarStyle==MatrixBarStyle.Coin && loaded.Widgets[0].RangeMinimum==-50 && loaded.Widgets[1].AnimationStyle==MatrixAnimationStyle.Cat && loaded.Widgets[1].Width==90 && loaded.Widgets[1].FlightSpeed==9 && loaded.Widgets[1].FlightDirection==MatrixFlightDirection.LeftToRight,"Graphic settings, constrained widths and six-dot height survive validation and restart.");
        if(previewPath is not null) System.IO.File.WriteAllText(previewPath,JsonSerializer.Serialize(previews));
        Console.WriteLine("PASS: 12 labelled bar styles at 30/45/60 widths and four heights, 11 animation-bank effects at 60/90 widths, rocket flame, bounds and persistence.");
    }
    private static long LeastCommonMultiple(int a,int b) { long x=a,y=b;while(y!=0){long t=x%y;x=y;y=t;}return a/(int)x*(long)b; }
    private static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
}
