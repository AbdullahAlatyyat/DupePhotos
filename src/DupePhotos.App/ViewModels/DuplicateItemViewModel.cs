using CommunityToolkit.Mvvm.ComponentModel;
using DupePhotos.Core;

namespace DupePhotos.App.ViewModels;

public sealed partial class DuplicateItemViewModel : ObservableObject
{
    public DuplicateItemViewModel(DuplicateImageItem item)
    {
        Path = item.Path;
        SizeBytes = item.SizeBytes;
        ModifiedAt = item.ModifiedAt;
        Width = item.Width;
        Height = item.Height;
        IsSuggestedKeep = item.IsSuggestedKeep;
    }

    [ObservableProperty]
    private bool isSelectedForDelete;

    public string Path { get; }

    public long SizeBytes { get; }

    public DateTimeOffset ModifiedAt { get; }

    public int Width { get; }

    public int Height { get; }

    public bool IsSuggestedKeep { get; }

    public string FileName => System.IO.Path.GetFileName(Path);

    public Uri ThumbnailUri => new(Path);

    public string Details => $"{Width} x {Height} | {FormatBytes(SizeBytes)} | {ModifiedAt.LocalDateTime:g}";

    public string KeepHint => IsSuggestedKeep ? "Suggested keep" : string.Empty;

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var suffix = 0;

        while (value >= 1024 && suffix < suffixes.Length - 1)
        {
            value /= 1024;
            suffix++;
        }

        return $"{value:0.#} {suffixes[suffix]}";
    }
}
