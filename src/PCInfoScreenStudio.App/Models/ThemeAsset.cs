using System.Text.Json.Serialization;

namespace PCInfoScreenStudio.Models;

public sealed class ThemeAsset : ObservableObject
{
    private string _displayName = string.Empty;
    private string _packagePath = string.Empty;
    private string _originalFileName = string.Empty;
    private string? _localPath;

    public Guid Id { get; set; } = Guid.NewGuid();
    public ThemeAssetKind Kind { get; set; }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public string PackagePath
    {
        get => _packagePath;
        set => SetProperty(ref _packagePath, value);
    }

    public string OriginalFileName
    {
        get => _originalFileName;
        set => SetProperty(ref _originalFileName, value);
    }

    [JsonIgnore]
    public string? LocalPath
    {
        get => _localPath;
        set => SetProperty(ref _localPath, value);
    }
}
