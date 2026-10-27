using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Rendering;

namespace PCInfoScreenStudio.ViewModels;

public sealed record MatrixArtChoice(string Key,string Label,ImageSource Image);
public sealed record MatrixBarChoice(MatrixBarStyle Value,string Label,ImageSource Image);

public sealed partial class MatrixWorkspaceViewModel
{
    private readonly DispatcherTimer _previewTimer = new() { Interval=TimeSpan.FromMilliseconds(200) };
    private bool _previewActive;
    private long _previewStep;
    public long PreviewAnimationStep => _previewStep;
    public IReadOnlyList<MatrixArtChoice> ArtChoices { get; private set; } = [];
    public IReadOnlyList<MatrixBarChoice> BarStyles { get; private set; } = [];
    public IReadOnlyList<MatrixFlightOption> FlightDirections { get; } = [new("Right to left",MatrixFlightDirection.RightToLeft),new("Left to right",MatrixFlightDirection.LeftToRight)];
    public RelayCommand AddProgressBarCommand { get; private set; } = null!;
    public RelayCommand AddBankArtCommand { get; private set; } = null!;
    public RelayCommand AddRocketCommand { get; private set; } = null!;
    private void InitializeGraphics()
    {
        ArtChoices=MatrixArtBank.All.Select(a=>new MatrixArtChoice(a.Key,a.Label,Thumbnail(MatrixArtBank.Get(a.Key,10).Pixels,MatrixArtBank.Get(a.Key,10).Width,10))).ToArray();
        BarStyles=MatrixBarPresets.All.Select(s=>new MatrixBarChoice(s.Value,s.Label,Thumbnail(MatrixGraphicsRenderer.Render(new() { Content=MatrixContent.ProgressBar,BarStyle=s.Value,Colour=s.Colour,Width=40,Height=10,UseSensor=false },MatrixWidgetReading.Unavailable,0),40,10))).ToArray();
        AddProgressBarCommand=new RelayCommand(AddBar,()=>Layers.Count<64);
        AddBankArtCommand=new RelayCommand(p=>AddBankArt(p as string ?? "heart"),_=>Layers.Count<64);
        AddRocketCommand=new RelayCommand(()=>AddBankArt("rocket",true),()=>Layers.Count<64);
        _previewTimer.Tick+=(_,_)=> { _previewStep++; PreviewChanged?.Invoke(this,EventArgs.Empty); };
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
        if(!_disposed && _previewActive && _draft.Widgets!.Any(w=>w.Visible && w.Content==MatrixContent.FlyingArt && w.AnimationPlaying)) _previewTimer.Start();
        else _previewTimer.Stop();
    }
    private static bool RequiresReading(MatrixWidgetSettings w) => w.Content==MatrixContent.Sensor || w.Content==MatrixContent.ProgressBar && w.UseSensor;
    private bool IsTypography => ContentKind is MatrixContent.Text or MatrixContent.Sensor or MatrixContent.Clock;
    public Visibility BarVisibility => ContentKind==MatrixContent.ProgressBar ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FlightVisibility => ContentKind==MatrixContent.FlyingArt ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ValueLabelVisibility => ContentKind==MatrixContent.Sensor ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ColourVisibility => ContentKind==MatrixContent.FlyingArt ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ManualBarVisibility => ContentKind==MatrixContent.ProgressBar && !UseSensor ? Visibility.Visible : Visibility.Collapsed;
    public bool UseSensor { get=>Tile.UseSensor; set { if(!IsEditable)return; Tile.UseSensor=value; if(!value)Tile.Alarm=false; RefreshFields();Changed(); } }
    public MatrixBarStyle BarStyle { get=>Tile.BarStyle; set { if(!IsEditable || !Enum.IsDefined(value))return;Tile.BarStyle=value;Tile.Colour=MatrixBarPresets.Get(value).Colour;RefreshFields();Changed(); } }
    public double RangeMinimum { get=>Tile.RangeMinimum; set { if(!IsEditable || !double.IsFinite(value))return;Tile.RangeMinimum=Math.Clamp(value,-1000000,999999);RaisePropertyChanged();Changed(); } }
    public double RangeMaximum { get=>Tile.RangeMaximum; set { if(!IsEditable || !double.IsFinite(value))return;Tile.RangeMaximum=Math.Clamp(value,-999999,1000000);RaisePropertyChanged();Changed(); } }
    public double ManualValue { get=>Tile.ManualValue; set { if(!IsEditable || !double.IsFinite(value))return;Tile.ManualValue=Math.Clamp(value,-1000000,1000000);RaisePropertyChanged();Changed(); } }
    public MatrixFlightDirection FlightDirection { get=>Tile.FlightDirection; set { if(!IsEditable || !Enum.IsDefined(value))return;Tile.FlightDirection=value;RaisePropertyChanged();Changed(); } }
    public int FlightSpeed { get=>Tile.FlightSpeed; set { if(!IsEditable)return;Tile.FlightSpeed=Math.Clamp(value,1,20);RaisePropertyChanged();Changed(); } }
    public bool AnimationPlaying { get=>Tile.AnimationPlaying; set { if(!IsEditable)return;Tile.AnimationPlaying=value;RaisePropertyChanged();Changed(); } }
    private void AddBar()
    {
        if(Layers.Count>=64)return;
        var w=new MatrixWidgetSettings { Content=MatrixContent.ProgressBar,Name="Progress bar",Source="CPU.Usage",Width=30,Height=5,AutoWidth=false,Scroll=false,Colour=MatrixBarPresets.All[0].Colour };
        _draft.Widgets!.Add(w);SelectedWidgetId=w.Id;Changed();
    }
    public void AddBankArt(string key,bool flight=false)
    {
        if(Layers.Count>=64 || !MatrixArtBank.All.Any(a=>a.Key==key))return;
        var art=MatrixArtBank.Get(key,10);
        var w=new MatrixWidgetSettings { Content=flight ? MatrixContent.FlyingArt : MatrixContent.PixelArt,Name=flight ? "Rocket flight" : MatrixArtBank.All.Single(a=>a.Key==key).Label,ArtKey=key,ArtWidth=art.Width,Width=flight ? 90 : art.Width,Height=10,Pixels=art.Pixels,AutoWidth=false,Scroll=false };
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
        foreach(var name in new[] {nameof(BarVisibility),nameof(FlightVisibility),nameof(ValueLabelVisibility),nameof(ColourVisibility),nameof(ManualBarVisibility),nameof(UseSensor),nameof(BarStyle),nameof(RangeMinimum),nameof(RangeMaximum),nameof(ManualValue),nameof(FlightDirection),nameof(FlightSpeed),nameof(AnimationPlaying)})RaisePropertyChanged(name);
    }
}
