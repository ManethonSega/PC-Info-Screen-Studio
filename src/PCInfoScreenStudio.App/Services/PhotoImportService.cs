using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Controllers;

namespace PCInfoScreenStudio.Services;

public static class PhotoImportService
{
    public static Task<IReadOnlyList<PhotoFrameItem>> PrepareAsync(Func<IEnumerable<string>> findFiles, CancellationToken ct, IProgress<string>? progress = null)
        => Task.Run<IReadOnlyList<PhotoFrameItem>>(() =>
        {
            var files = findFiles().Where(PhotoPlaybackController.IsSupportedPhoto).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var photos = new List<PhotoFrameItem>();
            for (var i = 0; i < files.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (i % 25 == 0) progress?.Report($"Reading photo {i + 1} of {files.Length}...");
                try
                {
                    var path = Path.GetFullPath(files[i]); if (!File.Exists(path)) continue;
                    var photo = new PhotoFrameItem { SourcePath = path };
                    PhotoMetadataService.Populate(photo, path); photos.Add(photo);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
            }
            ct.ThrowIfCancellationRequested(); return photos;
        }, ct);

}
