namespace DupePhotos.Core;

public sealed class ImageScanner : IImageScanner
{
    public static readonly ISet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".bmp",
        ".gif",
        ".webp",
        ".tif",
        ".tiff"
    };

    public IReadOnlyList<ImageFileCandidate> EnumerateImages(string folderPath, bool recursive = true)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ArgumentException("A folder path is required.", nameof(folderPath));
        }

        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException(folderPath);
        }

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = recursive,
            ReturnSpecialDirectories = false
        };

        return Directory.EnumerateFiles(folderPath, "*", options)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new ImageFileCandidate(path, info.Length, info.LastWriteTimeUtc);
            })
            .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
