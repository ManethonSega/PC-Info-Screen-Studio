using Microsoft.Win32;

namespace PCInfoScreenStudio.Services;

public sealed class FileDialogService
{
    public string? OpenTheme()
    {
        var initialDirectory = new ThemeLibraryService().UserThemesDirectory;
        Directory.CreateDirectory(initialDirectory);

        var dialog = new OpenFileDialog
        {
            Title = "Open theme",
            Filter = "PC Info Screen Studio theme (*.t3theme)|*.t3theme|All files (*.*)|*.*",
            InitialDirectory = initialDirectory
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SaveTheme(string suggestedName)
    {
        var initialDirectory = new ThemeLibraryService().UserThemesDirectory;
        Directory.CreateDirectory(initialDirectory);

        var dialog = new SaveFileDialog
        {
            Title = "Save theme",
            Filter = "PC Info Screen Studio theme (*.t3theme)|*.t3theme",
            FileName = SanitizeFileName(suggestedName) + ".t3theme",
            AddExtension = true,
            DefaultExt = ".t3theme",
            InitialDirectory = initialDirectory
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? OpenImage()
        => OpenFile("Add image", "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All files|*.*");

    public string[] OpenImages()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add photos",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All files|*.*",
            Multiselect = true
        };
        return dialog.ShowDialog() == true ? dialog.FileNames : [];
    }

    public string? OpenFolder()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose a photo folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    public string? SaveAlbumPreset(string suggestedName)
    {
        var directory = GetAlbumsDirectory();
        var dialog = new SaveFileDialog
        {
            Title = "Save photo album preset",
            Filter = "Photo album preset (*.pcalbum)|*.pcalbum",
            FileName = SanitizeFileName(suggestedName) + ".pcalbum",
            AddExtension = true,
            DefaultExt = ".pcalbum",
            InitialDirectory = directory
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? OpenAlbumPreset()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open photo album preset",
            Filter = "Photo album preset (*.pcalbum)|*.pcalbum",
            InitialDirectory = GetAlbumsDirectory()
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? OpenAnimatedImage()
        => OpenFile("Add animated image", "Animated GIF|*.gif|All files|*.*");

    public string? OpenVideo()
        => OpenFile("Add silent video", "Video|*.mp4;*.webm;*.mov;*.avi;*.mkv|All files|*.*");

    public string? OpenFont()
        => OpenFile("Add custom font", "Fonts|*.ttf;*.otf|All files|*.*");

    private static string? OpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return string.IsNullOrWhiteSpace(value) ? "theme" : value.Trim();
    }

    private static string GetAlbumsDirectory()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "PC Info Screen Studio",
            "Albums");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
