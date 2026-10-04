using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace PCInfoScreenStudio.Models;

public sealed class PhotoFrameSettings : ObservableObject
{
    private double _defaultDurationSeconds = 8;
    private double _transitionDurationSeconds = 1.2;
    private PhotoTransition _transition = PhotoTransition.Crossfade;
    private MediaFit _fit = MediaFit.Fit;
    private bool _loop = true;
    private bool _shuffle;
    private bool _embedImportedPhotos = true;
    private PhotoBackgroundMode _backgroundMode = PhotoBackgroundMode.BlurredImage;
    private string _backgroundColor = "#FF090B0F";
    private bool _showCaptions = true;
    private double _captionFontSize = 18;
    private string _captionColor = "#FFF7F8FA";
    private bool _watchFolderEnabled;
    private string _watchedFolder = string.Empty;
    private bool _scheduleEnabled;
    private string _infoScreenStart = "07:00";
    private string _photoFrameStart = "18:00";
    private string _screenOffStart = "23:30";
    private ScreenMode _eveningMode = ScreenMode.PhotoFrame;

    public ObservableCollection<PhotoFrameItem> Photos { get; set; } = [];
    public double DefaultDurationSeconds { get => _defaultDurationSeconds; set => SetProperty(ref _defaultDurationSeconds, Math.Clamp(value, 1, 3600)); }
    public double TransitionDurationSeconds { get => _transitionDurationSeconds; set => SetProperty(ref _transitionDurationSeconds, Math.Clamp(value, 0, 10)); }
    public PhotoTransition Transition { get => _transition; set => SetProperty(ref _transition, value); }
    public MediaFit Fit { get => _fit; set => SetProperty(ref _fit, value); }
    public bool Loop { get => _loop; set => SetProperty(ref _loop, value); }
    public bool Shuffle { get => _shuffle; set => SetProperty(ref _shuffle, value); }
    public bool EmbedImportedPhotos { get => _embedImportedPhotos; set => SetProperty(ref _embedImportedPhotos, value); }
    public PhotoBackgroundMode BackgroundMode { get => _backgroundMode; set => SetProperty(ref _backgroundMode, value); }
    public string BackgroundColor { get => _backgroundColor; set => SetProperty(ref _backgroundColor, value); }
    public bool ShowCaptions { get => _showCaptions; set => SetProperty(ref _showCaptions, value); }
    public double CaptionFontSize { get => _captionFontSize; set => SetProperty(ref _captionFontSize, Math.Clamp(value, 6, 100)); }
    public string CaptionColor { get => _captionColor; set => SetProperty(ref _captionColor, value); }
    public bool WatchFolderEnabled { get => _watchFolderEnabled; set => SetProperty(ref _watchFolderEnabled, value); }
    public string WatchedFolder { get => _watchedFolder; set => SetProperty(ref _watchedFolder, value); }
    public bool ScheduleEnabled { get => _scheduleEnabled; set => SetProperty(ref _scheduleEnabled, value); }
    public string InfoScreenStart { get => _infoScreenStart; set => SetProperty(ref _infoScreenStart, value); }
    public string PhotoFrameStart { get => _photoFrameStart; set => SetProperty(ref _photoFrameStart, value); }
    public string ScreenOffStart { get => _screenOffStart; set => SetProperty(ref _screenOffStart, value); }
    public ScreenMode EveningMode { get => _eveningMode; set => SetProperty(ref _eveningMode, value == ScreenMode.InfoScreen ? ScreenMode.PhotoFrame : value); }

    [JsonIgnore] public int RuntimeCurrentIndex { get; set; }
    [JsonIgnore] public int RuntimePreviousIndex { get; set; } = -1;
    [JsonIgnore] public double RuntimeTransitionProgress { get; set; } = 1;
    [JsonIgnore] public double RuntimePhotoProgress { get; set; }
    [JsonIgnore] public PhotoTransition RuntimeTransition { get; set; } = PhotoTransition.Crossfade;
}

public sealed class PhotoFrameItem : ObservableObject
{
    private string _displayName = string.Empty;
    private string _sourcePath = string.Empty;
    private Guid? _assetId;
    private double _durationSeconds;
    private PhotoTransition? _transitionOverride;
    private MediaFit? _fitOverride;
    private double _cropZoom = 1;
    private double _focalX = .5;
    private double _focalY = .5;
    private PhotoCaptionMode _captionMode = PhotoCaptionMode.FileName;
    private string _customCaption = string.Empty;
    private string _dateTaken = string.Empty;
    private string _location = string.Empty;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }
    public string SourcePath { get => _sourcePath; set => SetProperty(ref _sourcePath, value); }
    public Guid? AssetId
    {
        get => _assetId;
        set
        {
            if (!SetProperty(ref _assetId, value)) return;
            RaisePropertyChanged(nameof(IsEmbedded));
        }
    }
    public double DurationSeconds { get => _durationSeconds; set => SetProperty(ref _durationSeconds, Math.Clamp(value, 0, 3600)); }
    public PhotoTransition? TransitionOverride { get => _transitionOverride; set => SetProperty(ref _transitionOverride, value); }
    public MediaFit? FitOverride { get => _fitOverride; set => SetProperty(ref _fitOverride, value); }
    public double CropZoom { get => _cropZoom; set => SetProperty(ref _cropZoom, Math.Clamp(value, 1, 8)); }
    public double FocalX { get => _focalX; set => SetProperty(ref _focalX, Math.Clamp(value, 0, 1)); }
    public double FocalY { get => _focalY; set => SetProperty(ref _focalY, Math.Clamp(value, 0, 1)); }
    public PhotoCaptionMode CaptionMode { get => _captionMode; set => SetProperty(ref _captionMode, value); }
    public string CustomCaption { get => _customCaption; set => SetProperty(ref _customCaption, value); }
    public string DateTaken { get => _dateTaken; set => SetProperty(ref _dateTaken, value); }
    public string Location { get => _location; set => SetProperty(ref _location, value); }

    [JsonIgnore] public bool IsEmbedded => AssetId is not null;

    public string GetCaption()
        => CaptionMode switch
        {
            PhotoCaptionMode.None => string.Empty,
            PhotoCaptionMode.FileName => DisplayName,
            PhotoCaptionMode.DateTaken => string.IsNullOrWhiteSpace(DateTaken) ? DisplayName : DateTaken,
            PhotoCaptionMode.Location => string.IsNullOrWhiteSpace(Location) ? DisplayName : Location,
            PhotoCaptionMode.Custom => CustomCaption,
            _ => DisplayName
        };
}

public sealed class PhotoAlbumPreset
{
    public string Name { get; set; } = "Album";
    public PhotoFrameSettings Settings { get; set; } = new();
}
