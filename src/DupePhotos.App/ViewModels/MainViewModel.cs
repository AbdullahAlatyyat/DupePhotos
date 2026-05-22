using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DupePhotos.App.Services;
using DupePhotos.Core;

namespace DupePhotos.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IDuplicateDetector _detector;
    private readonly IRecycleBinService _recycleBin;
    private readonly Func<Task<string?>> _pickFolderAsync;
    private readonly Func<IReadOnlyList<string>, FileRemovalKind, Task<bool>> _confirmDeleteAsync;
    private CancellationTokenSource? _scanCancellation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelScanCommand))]
    private bool isScanning;

    [ObservableProperty]
    private string statusText = "Choose a folder to scan for duplicate images.";

    [ObservableProperty]
    private string? selectedFolder;

    [ObservableProperty]
    private double scanPercent;

    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = [];

    public MainViewModel(
        IDuplicateDetector detector,
        IRecycleBinService recycleBin,
        Func<Task<string?>> pickFolderAsync,
        Func<IReadOnlyList<string>, FileRemovalKind, Task<bool>>? confirmDeleteAsync = null)
    {
        _detector = detector;
        _recycleBin = recycleBin;
        _pickFolderAsync = pickFolderAsync;
        _confirmDeleteAsync = confirmDeleteAsync ?? ((_, _) => Task.FromResult(true));
    }

    [RelayCommand]
    private async Task PickFolderAsync()
    {
        var folder = await _pickFolderAsync();
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        SelectedFolder = folder;
        await ScanAsync(folder);
    }

    [RelayCommand(CanExecute = nameof(IsScanning))]
    private void CancelScan()
    {
        _scanCancellation?.Cancel();
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var selected = Groups
            .SelectMany(group => group.Items)
            .Where(item => item.IsSelectedForDelete)
            .Select(item => item.Path)
            .ToList();

        if (selected.Count == 0)
        {
            StatusText = "Select one or more duplicate files to recycle.";
            return;
        }

        var removalKind = _recycleBin is IFileRemovalPreview preview
            ? preview.GetRemovalKind()
            : FileRemovalKind.RecycleOrTrash;
        if (!await _confirmDeleteAsync(selected, removalKind))
        {
            StatusText = "Recycle canceled.";
            return;
        }

        await _recycleBin.MoveToRecycleBinAsync(selected);

        foreach (var group in Groups.ToList())
        {
            foreach (var item in group.Items.Where(item => selected.Contains(item.Path, StringComparer.Ordinal)).ToList())
            {
                group.Items.Remove(item);
            }

            if (group.Items.Count < 2)
            {
                Groups.Remove(group);
            }
        }

        StatusText = removalKind == FileRemovalKind.PermanentDelete
            ? $"Deleted {selected.Count} file(s)."
            : $"Moved {selected.Count} file(s) to the trash.";
    }

    private async Task ScanAsync(string folder)
    {
        _scanCancellation?.Cancel();
        _scanCancellation = new CancellationTokenSource();

        IsScanning = true;
        ScanPercent = 0;
        Groups.Clear();
        StatusText = "Scanning images...";

        var progress = new Progress<ScanProgress>(OnScanProgress);

        try
        {
            var groups = await _detector.FindDuplicatesAsync(folder, recursive: true, progress, _scanCancellation.Token);
            foreach (var group in groups)
            {
                Groups.Add(new DuplicateGroupViewModel(group));
            }

            StatusText = groups.Count == 0
                ? "No duplicate images found."
                : $"Found {groups.Count} duplicate group(s).";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan canceled.";
        }
        finally
        {
            IsScanning = false;
            ScanPercent = 0;
        }
    }

    private void OnScanProgress(ScanProgress progress)
    {
        if (progress.FilesDiscovered > 0)
        {
            ScanPercent = Math.Clamp(progress.FilesScanned * 100d / progress.FilesDiscovered, 0, 100);
        }

        if (progress.CurrentFile is not null)
        {
            StatusText = $"Scanned {progress.FilesScanned} of {progress.FilesDiscovered} images.";
        }
    }
}
