using DupePhotos.App.Services;

namespace DupePhotos.App.Tests;

public sealed class RecycleBinServiceTests
{
    [Fact]
    public void GetRemovalKind_UsesTrashWhenLinuxHasGio()
    {
        var service = new RecycleBinService(new FakePlatform { IsLinux = true }, new FakeCommandRunner("gio"));

        Assert.Equal(FileRemovalKind.RecycleOrTrash, service.GetRemovalKind());
    }

    [Fact]
    public void GetRemovalKind_UsesPermanentDeleteWhenLinuxHasNoGio()
    {
        var service = new RecycleBinService(new FakePlatform { IsLinux = true }, new FakeCommandRunner());

        Assert.Equal(FileRemovalKind.PermanentDelete, service.GetRemovalKind());
    }

    [Fact]
    public async Task MoveToRecycleBinAsync_OnLinuxRunsGioTrashWhenAvailable()
    {
        using var workspace = new TempWorkspace();
        var file = Path.Combine(workspace.Path, "duplicate.png");
        await File.WriteAllTextAsync(file, "image");
        var commands = new FakeCommandRunner("gio");
        var service = new RecycleBinService(new FakePlatform { IsLinux = true }, commands);

        await service.MoveToRecycleBinAsync([file]);

        var invocation = Assert.Single(commands.Invocations);
        Assert.Equal("gio", invocation.FileName);
        Assert.Equal(["trash", file], invocation.Arguments);
    }

    [Fact]
    public async Task MoveToRecycleBinAsync_DeletesPermanentlyWhenTrashIsUnavailable()
    {
        using var workspace = new TempWorkspace();
        var file = Path.Combine(workspace.Path, "duplicate.png");
        await File.WriteAllTextAsync(file, "image");
        var service = new RecycleBinService(new FakePlatform { IsLinux = true }, new FakeCommandRunner());

        await service.MoveToRecycleBinAsync([file]);

        Assert.False(File.Exists(file));
    }

    private sealed class FakePlatform : IPlatformEnvironment
    {
        public bool IsWindows { get; init; }

        public bool IsLinux { get; init; }

        public bool IsMacOS { get; init; }
    }

    private sealed class FakeCommandRunner(params string[] commands) : ICommandRunner
    {
        private readonly HashSet<string> _commands = new(commands, StringComparer.Ordinal);

        public List<CommandInvocation> Invocations { get; } = [];

        public bool CommandExists(string command)
        {
            return _commands.Contains(command);
        }

        public Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Invocations.Add(new CommandInvocation(fileName, arguments.ToArray()));
            return Task.FromResult(0);
        }
    }

    private sealed record CommandInvocation(string FileName, IReadOnlyList<string> Arguments);

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dupephotos-app-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
