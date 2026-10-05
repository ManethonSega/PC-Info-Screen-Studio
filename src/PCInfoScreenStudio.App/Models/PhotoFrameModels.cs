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
    private bool _embedImportedPhotos;
    private PhotoBackgroundMode _backgroundMode = PhotoBackgroundMode.SolidColor;
    private string _backgroundColor = "#FF090B0F";
    private bool _showCaptions = true;
    private double _captionFontSize = 18;
    private string _captionFontFamily = "Segoe UI";
    private string _captionColor = "#FFF7F8FA";
    private string _captionOutlineColor = "#FF000000";
    private double _captionOutlineThickness = 1.5;
    private PhotoCaptionMode _captionMode = PhotoCaptionMode.FileName;
    private string _customCaption = string.Empty;
    private bool _watchFolderEnabled;
    private string _watchedFolder = string.Empty;

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
    public string CaptionFontFamily { get => _captionFontFamily; set => SetProperty(ref _captionFontFamily, value); }
    public string CaptionColor { get => _captionColor; set => SetProperty(ref _captionColor, value); }
    public string CaptionOutlineColor { get => _captionOutlineColor; set => SetProperty(ref _captionOutlineColor, value); }
    public double CaptionOutlineThickness { get => _captionOutlineThickness; set => SetProperty(ref _captionOutlineThickness, Math.Clamp(value, 0, 10)); }
    public PhotoCaptionMode CaptionMode { get => _captionMode; set => SetProperty(ref _captionMode, value); }
    public string CustomCaption { get => _customCaption; set => SetProperty(ref _customCaption, value); }
    public bool WatchFolderEnabled { get => _watchFolderEnabled; set => SetProperty(ref _watchFolderEnabled, value); }
    public string WatchedFolder { get => _watchedFolder; set => SetProperty(ref _watchedFolder, value); }

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
    public string DateTaken { get => _dateTaken; set => SetProperty(ref _dateTaken, value); }
    public string Location { get => _location; set => SetProperty(ref _location, value); }

    [JsonIgnore] public bool IsEmbedded => AssetId is not null;

    public string GetCaption(PhotoCaptionMode captionMode, string customCaption)
        => captionMode switch
        {
            PhotoCaptionMode.None => string.Empty,
            PhotoCaptionMode.FileName => DisplayName,
            PhotoCaptionMode.DateTaken => DateTaken,
            PhotoCaptionMode.Location => Location,
            PhotoCaptionMode.Custom => customCaption,
            _ => string.Empty
        };
}

public sealed class PhotoAlbumPreset
{
    public string Name { get; set; } = "Album";
    public PhotoFrameSettings Settings { get; set; } = new();
}
