namespace DupePhotos.Core;

public enum DuplicateMatchKind
{
    ExactDuplicate,
    SameImageData,
    LikelyVisualDuplicate,
    NeedsReview
}

public sealed record ImageFileCandidate(
    string Path,
    long SizeBytes,
    DateTimeOffset ModifiedAt);

public sealed record ImageAnalysis(
    string Path,
    long SizeBytes,
    DateTimeOffset ModifiedAt,
    int Width,
    int Height,
    string Sha256,
    string PixelHash,
    ulong PerceptualHash,
    byte[] VerificationLuma);

public sealed record DuplicateImageItem(
    string Path,
    long SizeBytes,
    DateTimeOffset ModifiedAt,
    int Width,
    int Height,
    bool IsSuggestedKeep);

public sealed record DuplicateGroup(
    string Id,
    DuplicateMatchKind MatchKind,
    double Confidence,
    IReadOnlyList<DuplicateImageItem> Items);

public sealed record ScanProgress(
    int FilesDiscovered,
    int FilesScanned,
    int DuplicateGroupsFound,
    string? CurrentFile);
