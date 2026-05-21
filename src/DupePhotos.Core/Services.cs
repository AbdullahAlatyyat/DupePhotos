namespace DupePhotos.Core;

public interface IImageScanner
{
    IReadOnlyList<ImageFileCandidate> EnumerateImages(string folderPath, bool recursive = true);
}

public interface IImageHasher
{
    Task<ImageAnalysis> AnalyzeAsync(ImageFileCandidate file, CancellationToken cancellationToken = default);
}

public interface IDuplicateDetector
{
    Task<IReadOnlyList<DuplicateGroup>> FindDuplicatesAsync(
        string folderPath,
        bool recursive = true,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IRecycleBinService
{
    Task MoveToRecycleBinAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default);
}
