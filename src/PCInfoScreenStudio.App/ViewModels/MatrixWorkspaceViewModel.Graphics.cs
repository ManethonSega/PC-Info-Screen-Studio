using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Rendering;

namespace PCInfoScreenStudio.ViewModels;

public sealed record MatrixArtChoice(string Key,string Label,ImageSource Image);
public sealed record MatrixBarChoice(MatrixBarStyle Value,string Label,ImageSource Image);
public sealed record MatrixAnimationChoice(MatrixAnimationStyle Value,string Label,ImageSource Image);

public sealed partial class MatrixWorkspaceViewModel
{
    private readonly DispatcherTimer _previewTimer = new() { Interval=TimeSpan.FromMilliseconds(200) };
    private bool _previewActive;
    private long _previewStep;
    public long PreviewAnimationStep => _previewStep;
    public IReadOnlyList<MatrixArtChoice> ArtChoices { get; private set; } = [];
    public IReadOnlyList<MatrixBarChoice> BarStyles { get; private set; } = [];
    public IReadOnlyList<MatrixAnimationChoice> AnimationChoices { get; private set; } = [];
    public IReadOnlyList<MatrixWidgetSizeOption> BarWidths { get; } = [new("30 dots",30),new("45 dots",45),new("60 dots",60)];
    public IReadOnlyList<MatrixWidgetSizeOption> AnimationAreas { get; } = [new("60 dots · side panel",60),new("90 dots · side + front",90)];
    public IReadOnlyList<MatrixFlightOption> FlightDirections { get; } = [new("Right to left",MatrixFlightDirection.RightToLeft),new("Left to right",MatrixFlightDirection.LeftToRight)];
    public IReadOnlyList<MatrixCountOption> InstanceCounts { get; } = [new("1 object",1),new("2 objects",2),new("3 objects",3)];
    public IReadOnlyList<MatrixCatOption> CatStyles { get; } = [new("Beige",MatrixCatStyle.Beige),new("Black",MatrixCatStyle.Black),new("Gray",MatrixCatStyle.Gray),new("Orange",MatrixCatStyle.Orange),new("White",MatrixCatStyle.White)];
    public IReadOnlyList<MatrixStarOption> StarStyles { get; } = [new("White stars 1",MatrixStarStyle.White1),new("Yellow stars 1",MatrixStarStyle.Yellow1),new("White stars 2",MatrixStarStyle.White2)];
    public IReadOnlyList<MatrixShootingStarOption> ShootingStarStyles { get; } = [new("Off",MatrixShootingStarStyle.None),new("White shooting star",MatrixShootingStarStyle.White),new("Yellow shooting star",MatrixShootingStarStyle.Yellow)];
    public RelayCommand AddProgressBarCommand { get; private set; } = null!;
    public RelayCommand AddBankArtCommand { get; private set; } = null!;
    public RelayCommand AddAnimationCommand { get; private set; } = null!;
    public RelayCommand AddRocketCommand { get; private set; } = null!;
    private void InitializeGraphics()
    {
        ArtChoices=MatrixArtBank.All.Where(a=>a.Gallery).Select(a=>new MatrixArtChoice(a.Key,a.Label,Thumbnail(MatrixArtBank.Get(a.Key,10).Pixels,MatrixArtBank.Get(a.Key,10).Width,10))).ToArray();
        BarStyles=MatrixBarPresets.All.Select(s=>new MatrixBarChoice(s.Value,s.Label,Thumbnail(MatrixGraphicsRenderer.Render(new() { Content=MatrixContent.ProgressBar,BarStyle=s.Value,Colour=s.Colour,Width=45,Height=10,UseSensor=false },MatrixWidgetReading.Unavailable,0),45,10))).ToArray();
        AnimationChoices=MatrixAnimationPresets.All.Select(a=>new MatrixAnimationChoice(a.Value,a.Label,AnimationThumbnail(a))).ToArray();
        AddProgressBarCommand=new RelayCommand(AddBar,()=>Layers.Count<64);
        AddBankArtCommand=new RelayCommand(p=>AddBankArt(p as string ?? "heart"),_=>Layers.Count<64);
        AddAnimationCommand=new RelayCommand(p=>AddAnimation(p is MatrixAnimationStyle style ? style : MatrixAnimationStyle.Rocket),_=>Layers.Count<64);
        AddRocketCommand=new RelayCommand(()=>AddAnimation(MatrixAnimationStyle.Rocket),()=>Layers.Count<64);
        _previewTimer.Tick+=(_,_)=> { _previewStep++; PreviewChanged?.Invoke(this,EventArgs.Empty); };
    }
    private static ImageSource AnimationThumbnail(MatrixAnimationPreset preset)
    {
        var w=new MatrixWidgetSettings { Content=MatrixContent.FlyingArt,AnimationStyle=preset.Value,Name=preset.Label,Width=60,Height=10,AutoWidth=false,Colour=preset.Colour,FlightDirection=MatrixFlightDirection.LeftToRight };
        if(preset.Value==MatrixAnimationStyle.Rocket)
        {
            var art=MatrixArtBank.Get("rocket",10);w.ArtKey="rocket";w.ArtWidth=art.Width;w.Pixels=art.Pixels;
        }
        return Thumbnail(MatrixGraphicsRenderer.Render(w,MatrixWidgetReading.Unavailable,8),60,10);
    }
    private static ImageSource Thumbnail(byte[] pixels,int width,int height)
    {
        var cropped=new byte[width*height*3];
        for(int y=0;y<height;y++) Array.Copy(pixels,y*270,cropped,y*width*3,width*3);
        var bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Rgb24,null,cropped,width*3); bitmap.Freeze(); return bitmap;
    }
    public void SetPreviewActive(bool active) { _previewActive=active; UpdatePreviewTimer(); }
    private void UpdatePreviewTimer()
    {
        if(!_disposed && _previewActive && _draft.Widgets!.Any(w=>w.Visible && w.AnimationPlaying && (w.Content==MatrixContent.FlyingArt || w.Content==MatrixContent.Text && w.Scroll))) _previewTimer.Start();
        else _previewTimer.Stop();
    }
    private static bool RequiresReading(MatrixWidgetSettings w) => w.Content==MatrixContent.Sensor || w.Content==MatrixContent.ProgressBar && w.UseSensor;
    private bool IsTypography => ContentKind is MatrixContent.Text or MatrixContent.Sensor or MatrixContent.Clock;
    private bool HasFontChoice => IsTypography || ContentKind==MatrixContent.ProgressBar;
    public Visibility BarVisibility => ContentKind==MatrixContent.ProgressBar ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FlightVisibility => ContentKind==MatrixContent.FlyingArt ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TextAnimationVisibility => ContentKind==MatrixContent.Text ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AnimationAreaVisibility => ContentKind==MatrixContent.FlyingArt || ContentKind==MatrixContent.Text && Scroll ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InstanceCountVisibility => ContentKind==MatrixContent.FlyingArt && MatrixAnimationPresets.SupportsInstances(Tile.AnimationStyle) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CatStyleVisibility => ContentKind==MatrixContent.FlyingArt && Tile.AnimationStyle==MatrixAnimationStyle.Cat ? Visibility.Visible : Visibility.Collapsed;
    public Visibility StarStyleVisibility => ContentKind==MatrixContent.FlyingArt && Tile.AnimationStyle==MatrixAnimationStyle.Starfield ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ValueLabelVisibility => ContentKind==MatrixContent.Sensor ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ColourVisibility => ContentKind==MatrixContent.FlyingArt && Tile.AnimationStyle is MatrixAnimationStyle.Rocket or MatrixAnimationStyle.Cat or MatrixAnimationStyle.Starfield or MatrixAnimationStyle.Ufo ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ManualBarVisibility => ContentKind==MatrixContent.ProgressBar && !UseSensor ? Visibility.Visible : Visibility.Collapsed;
    public bool UseSensor { get=>Tile.UseSensor; set { if(!IsEditable)return; Tile.UseSensor=value; if(!value)Tile.Alarm=false; RefreshFields();Changed(); } }
    public MatrixBarStyle BarStyle { get=>Tile.BarStyle; set { if(!IsEditable || !Enum.IsDefined(value))return;Tile.BarStyle=value;Tile.Colour=MatrixBarPresets.Get(value).Colour;RefreshFields();Changed(); } }
    public double RangeMinimum { get=>Tile.RangeMinimum; set { if(!IsEditable || !double.IsFinite(value))return;Tile.RangeMinimum=Math.Clamp(value,-1000000,999999);RaisePropertyChanged();Changed(); } }
    public double RangeMaximum { get=>Tile.RangeMaximum; set { if(!IsEditable || !double.IsFinite(value))return;Tile.RangeMaximum=Math.Clamp(value,-999999,1000000);RaisePropertyChanged();Changed(); } }
    public double ManualValue { get=>Tile.ManualValue; set { if(!IsEditable || !double.IsFinite(value))return;Tile.ManualValue=Math.Clamp(value,-1000000,1000000);RaisePropertyChanged();Changed(); } }
    public MatrixFlightDirection FlightDirection { get=>Tile.FlightDirection; set { if(!IsEditable || !Enum.IsDefined(value))return;Tile.FlightDirection=value;RaisePropertyChanged();Changed(); } }
    public int FlightSpeed { get=>Tile.FlightSpeed; set { if(!IsEditable)return;Tile.FlightSpeed=Math.Clamp(value,1,20);RaisePropertyChanged();Changed(); } }
    public bool AnimationPlaying { get=>Tile.AnimationPlaying; set { if(!IsEditable)return;Tile.AnimationPlaying=value;RaisePropertyChanged();Changed(); } }
    public int InstanceCount { get=>Tile.InstanceCount; set { if(!IsEditable)return;Tile.InstanceCount=Math.Clamp(value,1,3);RaisePropertyChanged();Changed(); } }
    public MatrixCatStyle CatStyle { get=>Tile.CatStyle; set { if(!IsEditable || !Enum.IsDefined(value))return;Tile.CatStyle=value;RaisePropertyChanged();Changed(); } }
    public MatrixStarStyle StarStyle { get=>Tile.StarStyle; set { if(!IsEditable || !Enum.IsDefined(value))return;Tile.StarStyle=value;RaisePropertyChanged();Changed(); } }
    public MatrixShootingStarStyle ShootingStarStyle { get=>Tile.ShootingStarStyle; set { if(!IsEditable || !Enum.IsDefined(value))return;Tile.ShootingStarStyle=value;RaisePropertyChanged();Changed(); } }
    public int BarWidth { get=>Tile.Width; set { if(!IsEditable || value is not (30 or 45 or 60))return;Width=value;RaisePropertyChanged(); } }
    public int AnimationAreaWidth { get=>Tile.Width; set { if(!IsEditable || value is not (60 or 90))return;Width=value;RaisePropertyChanged(); } }
    private void AddBar()
    {
        if(Layers.Count>=64)return;
        var w=new MatrixWidgetSettings { Content=MatrixContent.ProgressBar,Name="Progress bar",Source="CPU.Usage",Width=30,Height=5,AutoWidth=false,Scroll=false,Colour=MatrixBarPresets.All[0].Colour };
        _draft.Widgets!.Add(w);SelectedWidgetId=w.Id;Changed();
    }
    public void AddBankArt(string key)
    {
        if(Layers.Count>=64 || !MatrixArtBank.All.Any(a=>a.Key==key))return;
        var art=MatrixArtBank.Get(key,10);
        var w=new MatrixWidgetSettings { Content=MatrixContent.PixelArt,Name=MatrixArtBank.All.Single(a=>a.Key==key).Label,ArtKey=key,ArtWidth=art.Width,Width=art.Width,Height=10,Pixels=art.Pixels,AutoWidth=false,Scroll=false };
        _draft.Widgets!.Add(w);SelectedWidgetId=w.Id;Changed();
    }
    public void AddAnimation(MatrixAnimationStyle style)
    {
        if(Layers.Count>=64 || !Enum.IsDefined(style))return;
        var preset=MatrixAnimationPresets.Get(style);
        var w=new MatrixWidgetSettings { Content=MatrixContent.FlyingArt,AnimationStyle=style,Name=preset.Label,Width=90,Height=10,AutoWidth=false,Scroll=false,Colour=preset.Colour };
        if(style==MatrixAnimationStyle.Rocket)
        {
            var art=MatrixArtBank.Get("rocket",10);w.ArtKey="rocket";w.ArtWidth=art.Width;w.Pixels=art.Pixels;
        }
        _draft.Widgets!.Add(w);SelectedWidgetId=w.Id;Changed();
    }
    private void ResizeBankArt()
    {
        if(Tile.ArtKey.Length==0 || !MatrixArtBank.All.Any(a=>a.Key==Tile.ArtKey))return;
        var art=MatrixArtBank.Get(Tile.ArtKey,Tile.Height);Tile.ArtWidth=art.Width;Tile.Pixels=art.Pixels;
        if(ContentKind==MatrixContent.PixelArt)Tile.Width=art.Width;
        Tile.X=Math.Min(Tile.X,90-Tile.Width);
    }
    private void RefreshGraphicsFields()
    {
        foreach(var name in new[] {nameof(BarVisibility),nameof(FlightVisibility),nameof(TextAnimationVisibility),nameof(AnimationAreaVisibility),nameof(InstanceCountVisibility),nameof(CatStyleVisibility),nameof(StarStyleVisibility),nameof(ValueLabelVisibility),nameof(ColourVisibility),nameof(ManualBarVisibility),nameof(UseSensor),nameof(BarStyle),nameof(BarWidth),nameof(AnimationAreaWidth),nameof(RangeMinimum),nameof(RangeMaximum),nameof(ManualValue),nameof(FlightDirection),nameof(FlightSpeed),nameof(AnimationPlaying),nameof(InstanceCount),nameof(CatStyle),nameof(StarStyle),nameof(ShootingStarStyle)})RaisePropertyChanged(name);
    }
}
