using DupePhotos.App.Services;
using DupePhotos.App.ViewModels;
using DupePhotos.Core;

namespace DupePhotos.App.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task PickFolderCommand_ScansSelectedFolderAndShowsGroups()
    {
        var detector = new FakeDuplicateDetector
        {
            Groups =
            [
                CreateGroup("/photos/a.png", "/photos/b.png")
            ]
        };
        var viewModel = new MainViewModel(detector, new FakeRecycleBinService(), () => Task.FromResult<string?>("/photos"));

        await viewModel.PickFolderCommand.ExecuteAsync(null);

        Assert.Equal("/photos", viewModel.SelectedFolder);
        Assert.Single(viewModel.Groups);
        Assert.Equal("Found 1 duplicate group(s).", viewModel.StatusText);
    }

    [Fact]
    public async Task CancelScanCommand_CancelsActiveScan()
    {
        var detector = new BlockingDuplicateDetector();
        var viewModel = new MainViewModel(detector, new FakeRecycleBinService(), () => Task.FromResult<string?>("/photos"));

        var scanTask = viewModel.PickFolderCommand.ExecuteAsync(null);
        await detector.Started.Task;

        viewModel.CancelScanCommand.Execute(null);
        await scanTask;

        Assert.False(viewModel.IsScanning);
        Assert.Equal("Scan canceled.", viewModel.StatusText);
    }

    [Fact]
    public void AutoSelectDuplicatesCommand_SelectsEverythingExceptSuggestedKeeps()
    {
        var viewModel = new MainViewModel(new FakeDuplicateDetector(), new FakeRecycleBinService(), () => Task.FromResult<string?>(null));
        viewModel.Groups.Add(new DuplicateGroupViewModel(CreateGroup("/photos/a.png", "/photos/b.png", "/photos/c.png")));

        viewModel.AutoSelectDuplicatesCommand.Execute(null);

        var items = viewModel.Groups.Single().Items;
        Assert.False(items[0].IsSelectedForDelete);
        Assert.True(items[1].IsSelectedForDelete);
        Assert.True(items[2].IsSelectedForDelete);
        Assert.Equal("Auto selected 2 duplicate file(s).", viewModel.StatusText);
    }

    [Fact]
    public async Task DeleteSelectedCommand_RemovesDeletedItemsAndEmptyGroups()
    {
        var recycleBin = new FakeRecycleBinService();
        var group = new DuplicateGroupViewModel(CreateGroup("/photos/a.png", "/photos/b.png"));
        group.Items[1].IsSelectedForDelete = true;
        var viewModel = new MainViewModel(
            new FakeDuplicateDetector(),
            recycleBin,
            () => Task.FromResult<string?>(null),
            (_, _) => Task.FromResult(true));
        viewModel.Groups.Add(group);

        await viewModel.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Groups);
        Assert.Equal(["/photos/b.png"], recycleBin.MovedPaths);
        Assert.Equal("Moved 1 file(s) to the trash.", viewModel.StatusText);
    }

    [Fact]
    public async Task DeleteSelectedCommand_DoesNotRecycleWhenNothingIsSelected()
    {
        var recycleBin = new FakeRecycleBinService();
        var viewModel = new MainViewModel(new FakeDuplicateDetector(), recycleBin, () => Task.FromResult<string?>(null));
        viewModel.Groups.Add(new DuplicateGroupViewModel(CreateGroup("/photos/a.png", "/photos/b.png")));

        await viewModel.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.Empty(recycleBin.MovedPaths);
        Assert.Equal("Select one or more duplicate files to recycle.", viewModel.StatusText);
    }

    [Fact]
    public async Task DeleteSelectedCommand_StopsWhenConfirmationIsDeclined()
    {
        var recycleBin = new FakeRecycleBinService();
        var group = new DuplicateGroupViewModel(CreateGroup("/photos/a.png", "/photos/b.png"));
        group.Items[0].IsSelectedForDelete = true;
        var viewModel = new MainViewModel(
            new FakeDuplicateDetector(),
            recycleBin,
            () => Task.FromResult<string?>(null),
            (_, _) => Task.FromResult(false));
        viewModel.Groups.Add(group);

        await viewModel.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.Empty(recycleBin.MovedPaths);
        Assert.Single(viewModel.Groups);
        Assert.Equal("Recycle canceled.", viewModel.StatusText);
    }

    private static DuplicateGroup CreateGroup(params string[] paths)
    {
        var items = paths
            .Select((path, index) => new DuplicateImageItem(path, 1024, DateTimeOffset.UnixEpoch, 32, 32, index == 0))
            .ToArray();
        return new DuplicateGroup("group-1", DuplicateMatchKind.ExactDuplicate, 1.0, items);
    }

    private sealed class FakeDuplicateDetector : IDuplicateDetector
    {
        public IReadOnlyList<DuplicateGroup> Groups { get; init; } = [];

        public Task<IReadOnlyList<DuplicateGroup>> FindDuplicatesAsync(
            string folderPath,
            bool recursive = true,
            IProgress<ScanProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Report(new ScanProgress(2, 2, Groups.Count, null));
            return Task.FromResult(Groups);
        }
    }

    private sealed class BlockingDuplicateDetector : IDuplicateDetector
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<DuplicateGroup>> FindDuplicatesAsync(
            string folderPath,
            bool recursive = true,
            IProgress<ScanProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return [];
        }
    }

    private sealed class FakeRecycleBinService : IRecycleBinService
    {
        public List<string> MovedPaths { get; } = [];

        public Task MoveToRecycleBinAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
        {
            MovedPaths.AddRange(filePaths);
            return Task.CompletedTask;
        }
    }
}
