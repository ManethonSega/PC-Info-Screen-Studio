using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;
using PCInfoScreenStudio.Rendering;

namespace PCInfoScreenStudio.Controllers;

/// <summary>Owns the linked playlist, slideshow timing, transitions and folder watching.</summary>
public sealed class PhotoPlaybackController : ObservableObject, IDisposable
{
    private readonly Func<ThemeWorkspace> _workspace;
    private readonly SettingsController _settings;
    private readonly FileDialogService _dialogs = new();
    private readonly PhotoAlbumPresetService _photoAlbumPresetService = new();
    private FileSystemWatcher? _photoFolderWatcher;
    private PhotoFrameItem? _selectedPhoto;
    private bool _isPhotoPlaying;
    private bool _disposed;
    private DateTimeOffset _photoStartedAt;
    private DateTimeOffset _photoTransitionStartedAt;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Random _photoRandom = new();

    public PhotoPlaybackController(Func<ThemeWorkspace> workspace, SettingsController settings,
        Func<DateTimeOffset>? utcNow = null)
    {
        _workspace = workspace;
        _settings = settings;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _photoStartedAt = _utcNow();
        _photoTransitionStartedAt = _photoStartedAt;
        _isPhotoPlaying = settings.PhotoFramePlaying;
    }

    private ThemeWorkspace Workspace => _workspace();
    private ThemeDocument Document => Workspace.Document;
    private IEnumerable<WidgetModel> AllWidgets => Document.Widgets.Concat(Document.HybridWidgets);

    public event EventHandler? DocumentChanged;
    public event EventHandler? PreviewChanged;
    public event EventHandler? LiveFrameRequested;
    public event EventHandler? CommandStatesChanged;
    public event Action<string>? StatusReported;
    public event Action<PhotoFrameSettings>? SettingsReplacementRequested;

    public PhotoFrameItem? SelectedPhoto
    {
        get => _selectedPhoto;
        set
        {
            if (!SetProperty(ref _selectedPhoto, value)) return;
            CommandStatesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsPhotoPlaying => _isPhotoPlaying;
    public string PhotoPlaybackLabel => _isPhotoPlaying ? "Pause" : "Play";
    public string PhotoPositionLabel => Document.PhotoFrame.Photos.Count == 0
        ? "No photos"
        : $"{Document.PhotoFrame.RuntimeCurrentIndex + 1} / {Document.PhotoFrame.Photos.Count}";

    public void AddPhotos()
    {
        var files = _dialogs.OpenImages();
        if (files.Length > 0)
            AddPhotoFiles(files);
    }

    public void AddPhotoFolder()
    {
        var folder = _dialogs.OpenFolder();
        if (string.IsNullOrWhiteSpace(folder)) return;
        AddPhotoFolder(folder);
    }

    public void AddPhotoFolder(string folder)
    {
        Document.PhotoFrame.WatchedFolder = Path.GetFullPath(folder);
        AddPhotoFiles(GetPhotoFiles(Document.PhotoFrame.WatchedFolder));
        ConfigurePhotoFolderWatcher();
    }

    public string? RestoreThemeFolder()
    {
        var folder = Document.PhotoFrame.WatchedFolder;
        string? warning = null;
        if (!string.IsNullOrWhiteSpace(folder))
        {
            // Enumerate before replacing the playlist, so an inaccessible folder cannot erase it.
            var files = GetPhotoFiles(folder).ToArray();
            foreach (var photo in Document.PhotoFrame.Photos.ToArray())
                Document.PhotoFrame.Photos.Remove(photo);
            if (Directory.Exists(folder)) AddPhotoFiles(files);
            else warning = "Photo folder unavailable. Choose its new location using Add folder.";
        }
        InitializePhotoFrameRuntime();
        RaisePropertyChanged(nameof(PhotoPositionLabel));
        return warning;
    }

    public void AddPhotoFiles(IEnumerable<string> files)
    {
        var added = new List<PhotoFrameItem>();
        foreach (var path in files.Where(IsSupportedPhoto).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath)) continue;
                if (Document.PhotoFrame.Photos.Any(p => p.AssetId is null && string.Equals(Path.GetFullPath(p.SourcePath), fullPath, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var item = new PhotoFrameItem { SourcePath = fullPath };
                PhotoMetadataService.Populate(item, fullPath);
                Document.PhotoFrame.Photos.Add(item);
                added.Add(item);
            }
            catch (Exception ex)
            {
                StatusReported?.Invoke($"Skipped photo: {Path.GetFileName(path)} ({ex.Message})");
            }
        }

        if (added.Count == 0) return;
        SelectedPhoto = added[0];
        if (Document.PhotoFrame.Photos.Count == added.Count)
            SetCurrentPhoto(0, manual: true);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        CommandStatesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveSelectedPhoto()
    {
        var photo = SelectedPhoto;
        if (photo is null) return;
        var current = Document.PhotoFrame.Photos.ElementAtOrDefault(Document.PhotoFrame.RuntimeCurrentIndex);
        var index = Document.PhotoFrame.Photos.IndexOf(photo);
        Document.PhotoFrame.Photos.Remove(photo);
        if (photo.AssetId is Guid assetId && !Document.PhotoFrame.Photos.Any(p => p.AssetId == assetId) && !AllWidgets.Any(w => w.AssetId == assetId))
        {
            var asset = Document.Assets.FirstOrDefault(a => a.Id == assetId);
            if (asset is not null) Document.Assets.Remove(asset);
        }
        SelectedPhoto = Document.PhotoFrame.Photos.Count == 0
            ? null
            : Document.PhotoFrame.Photos[Math.Min(index, Document.PhotoFrame.Photos.Count - 1)];
        Document.PhotoFrame.RuntimeCurrentIndex = current is not null && !ReferenceEquals(current, photo)
            ? Math.Max(0, Document.PhotoFrame.Photos.IndexOf(current))
            : Math.Clamp(index, 0, Math.Max(0, Document.PhotoFrame.Photos.Count - 1));
        Document.PhotoFrame.RuntimePreviousIndex = -1;
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        CommandStatesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MovePhoto(PhotoFrameItem source, PhotoFrameItem target)
    {
        var current = Document.PhotoFrame.Photos.ElementAtOrDefault(Document.PhotoFrame.RuntimeCurrentIndex);
        var previous = Document.PhotoFrame.Photos.ElementAtOrDefault(Document.PhotoFrame.RuntimePreviousIndex);
        var oldIndex = Document.PhotoFrame.Photos.IndexOf(source);
        var newIndex = Document.PhotoFrame.Photos.IndexOf(target);
        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex) return;
        Document.PhotoFrame.Photos.Move(oldIndex, newIndex);
        Document.PhotoFrame.RuntimeCurrentIndex = current is null ? 0 : Document.PhotoFrame.Photos.IndexOf(current);
        Document.PhotoFrame.RuntimePreviousIndex = previous is null ? -1 : Document.PhotoFrame.Photos.IndexOf(previous);
        SelectedPhoto = source;
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        RaisePropertyChanged(nameof(PhotoPositionLabel));
    }

    public void PreviousPhoto() => MovePhotoBy(-1);
    public void NextPhoto() => MovePhotoBy(1);

    private void MovePhotoBy(int direction)
    {
        var count = Document.PhotoFrame.Photos.Count;
        if (count == 0) return;
        var next = (Document.PhotoFrame.RuntimeCurrentIndex + direction + count) % count;
        SetCurrentPhoto(next, manual: true);
    }

    public void TogglePhotoPlayback()
    {
        _isPhotoPlaying = !_isPhotoPlaying;
        _settings.PhotoFramePlaying = _isPhotoPlaying;
        _photoStartedAt = _utcNow();
        RaisePropertyChanged(nameof(IsPhotoPlaying));
        RaisePropertyChanged(nameof(PhotoPlaybackLabel));
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SaveAlbumPreset()
    {
        var path = _dialogs.SaveAlbumPreset(Document.Name + " album");
        if (path is null) return;
        try
        {
            _photoAlbumPresetService.Save(Workspace, path);
            StatusReported?.Invoke("Album preset saved");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not save album preset", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void LoadAlbumPreset()
    {
        var path = _dialogs.OpenAlbumPreset();
        if (path is null) return;
        try
        {
            SettingsReplacementRequested?.Invoke(_photoAlbumPresetService.Load(path));
            StatusReported?.Invoke("Album preset loaded");
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not load album preset", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void ChooseWatchedFolder()
    {
        var folder = _dialogs.OpenFolder();
        if (folder is null) return;
        Document.PhotoFrame.WatchedFolder = folder;
        Document.PhotoFrame.WatchFolderEnabled = true;
        AddPhotoFiles(GetPhotoFiles(folder));
        ConfigurePhotoFolderWatcher();
    }

    private static IEnumerable<string> GetPhotoFiles(string folder)
        => Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories).Where(IsSupportedPhoto).OrderBy(Path.GetFileName)
            : [];

    public static bool IsSupportedPhoto(string path)
        => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp";

    public void InitializePhotoFrameRuntime()
    {
        var settings = Document.PhotoFrame;
        settings.EmbedImportedPhotos = false;
        settings.BackgroundMode = PhotoBackgroundMode.SolidColor;
        if (settings.Transition == PhotoTransition.KenBurns)
            settings.Transition = PhotoTransition.Crossfade;
        settings.RuntimeCurrentIndex = Math.Clamp(_settings.LastPhotoIndex, 0, Math.Max(0, settings.Photos.Count - 1));
        settings.RuntimePreviousIndex = -1;
        settings.RuntimeTransitionProgress = 1;
        settings.RuntimePhotoProgress = 0;
        settings.RuntimeTransition = ResolveTransition();
        _photoStartedAt = _utcNow();
        _photoTransitionStartedAt = _photoStartedAt;
        SelectedPhoto = settings.Photos.ElementAtOrDefault(settings.RuntimeCurrentIndex);
        UpdateEffectiveScreenMode(force: true);
        ConfigurePhotoFolderWatcher();
        PreloadUpcomingPhoto();
        CommandStatesChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool UpdatePhotoPlayback()
    {
        var settings = Document.PhotoFrame;
        if (settings.Photos.Count == 0) return false;
        var now = _utcNow();
        var duration = TimeSpan.FromSeconds(settings.DefaultDurationSeconds);
        var elapsed = now - _photoStartedAt;

        if (_isPhotoPlaying && elapsed >= duration)
        {
            if (!settings.Loop && !settings.Shuffle && settings.RuntimeCurrentIndex >= settings.Photos.Count - 1)
            {
                _isPhotoPlaying = false;
                RaisePropertyChanged(nameof(IsPhotoPlaying));
                RaisePropertyChanged(nameof(PhotoPlaybackLabel));
            }
            else
            {
                var next = settings.Shuffle && settings.Photos.Count > 1
                    ? NextRandomPhoto(settings.RuntimeCurrentIndex, settings.Photos.Count)
                    : (settings.RuntimeCurrentIndex + 1) % settings.Photos.Count;
                SetCurrentPhoto(next, manual: false);
                return true;
            }
        }

        var transitionDuration = Math.Min(duration.TotalSeconds, settings.TransitionDurationSeconds);
        settings.RuntimeTransitionProgress = transitionDuration <= 0
            ? 1
            : Math.Clamp((now - _photoTransitionStartedAt).TotalSeconds / transitionDuration, 0, 1);
        settings.RuntimePhotoProgress = duration.TotalSeconds <= 0
            ? 1
            : Math.Clamp(elapsed.TotalSeconds / duration.TotalSeconds, 0, 1);
        return settings.RuntimeTransitionProgress < 1;
    }

    public void SetCurrentPhoto(int index, bool manual)
    {
        var settings = Document.PhotoFrame;
        if (settings.Photos.Count == 0) return;
        index = Math.Clamp(index, 0, settings.Photos.Count - 1);
        settings.RuntimePreviousIndex = settings.RuntimeCurrentIndex;
        settings.RuntimeCurrentIndex = index;
        settings.RuntimeTransition = ResolveTransition();
        settings.RuntimeTransitionProgress = settings.RuntimeTransition == PhotoTransition.Instant ? 1 : 0;
        settings.RuntimePhotoProgress = 0;
        _photoStartedAt = _utcNow();
        _photoTransitionStartedAt = _photoStartedAt;
        _settings.LastPhotoIndex = index;
        SelectedPhoto = settings.Photos[index];
        RaisePropertyChanged(nameof(PhotoPositionLabel));
        PreviewChanged?.Invoke(this, EventArgs.Empty);
        LiveFrameRequested?.Invoke(this, EventArgs.Empty);
        PreloadUpcomingPhoto();
    }

    private PhotoTransition ResolveTransition()
    {
        var transition = Document.PhotoFrame.Transition;
        if (transition == PhotoTransition.KenBurns) transition = PhotoTransition.Crossfade;
        if (transition != PhotoTransition.Random) return transition;
        var choices = new[] { PhotoTransition.Crossfade, PhotoTransition.Slide, PhotoTransition.Zoom, PhotoTransition.Instant };
        return choices[_photoRandom.Next(choices.Length)];
    }

    private int NextRandomPhoto(int current, int count)
    {
        var next = _photoRandom.Next(count - 1);
        return next >= current ? next + 1 : next;
    }

    private void PreloadUpcomingPhoto()
    {
        var settings = Document.PhotoFrame;
        if (settings.Photos.Count == 0) return;
        var next = settings.Shuffle && settings.Photos.Count > 1
            ? NextRandomPhoto(settings.RuntimeCurrentIndex, settings.Photos.Count)
            : (settings.RuntimeCurrentIndex + 1) % settings.Photos.Count;
        var workspace = Workspace;
        var item = settings.Photos[next];
        _ = Task.Run(() =>
        {
            try { ThemeRenderer.PreloadPhoto(workspace, item); } catch { }
        });
    }

    public bool UpdateEffectiveScreenMode(bool force = false)
    {
        var next = MapRuntimeMode(Document.Mode);
        if (!force && Document.RuntimeMode == next) return false;
        Document.RuntimeMode = next;
        PreviewChanged?.Invoke(this, EventArgs.Empty);
        LiveFrameRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private static RuntimeScreenMode MapRuntimeMode(ScreenMode mode)
        => mode switch
        {
            ScreenMode.PhotoFrame => RuntimeScreenMode.PhotoFrame,
            ScreenMode.Hybrid => RuntimeScreenMode.Hybrid,
            _ => RuntimeScreenMode.InfoScreen
        };

    public void ConfigurePhotoFolderWatcher()
    {
        _photoFolderWatcher?.Dispose();
        _photoFolderWatcher = null;
        var settings = Document.PhotoFrame;
        if (!settings.WatchFolderEnabled || !Directory.Exists(settings.WatchedFolder)) return;

        _photoFolderWatcher = new FileSystemWatcher(settings.WatchedFolder)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };
        _photoFolderWatcher.Created += OnWatchedPhotoChanged;
        _photoFolderWatcher.Renamed += OnWatchedPhotoChanged;
    }

    private void OnWatchedPhotoChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsSupportedPhoto(e.FullPath)) return;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (!_disposed) AddPhotoFiles([e.FullPath]);
        });
    }


    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _photoFolderWatcher?.Dispose();
        _photoFolderWatcher = null;
    }
}
