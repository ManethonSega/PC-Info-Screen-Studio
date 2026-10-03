using Microsoft.Win32;

namespace PCInfoScreenStudio.Services;

public sealed class FileDialogService
{
    public string? OpenTheme()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open theme",
            Filter = "PC Info Screen Studio theme (*.t3theme)|*.t3theme|All files (*.*)|*.*"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SaveTheme(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save theme",
            Filter = "PC Info Screen Studio theme (*.t3theme)|*.t3theme",
            FileName = SanitizeFileName(suggestedName) + ".t3theme",
            AddExtension = true,
            DefaultExt = ".t3theme"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? OpenImage()
        => OpenFile("Add image", "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All files|*.*");

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
}
