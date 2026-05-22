using System.Diagnostics;
using System.Runtime.InteropServices;
using DupePhotos.Core;

namespace DupePhotos.App.Services;

public enum FileRemovalKind
{
    RecycleOrTrash,
    PermanentDelete
}

public interface IFileRemovalPreview
{
    FileRemovalKind GetRemovalKind();
}

public interface IPlatformEnvironment
{
    bool IsWindows { get; }

    bool IsLinux { get; }

    bool IsMacOS { get; }
}

public interface ICommandRunner
{
    bool CommandExists(string command);

    Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

public sealed class RuntimePlatformEnvironment : IPlatformEnvironment
{
    public bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
}

public sealed class ProcessCommandRunner : ICommandRunner
{
    public bool CommandExists(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return path
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, command))
            .Any(File.Exists);
    }

    public async Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            CreateNoWindow = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {fileName}.");

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }
}

public sealed class RecycleBinService : IRecycleBinService, IFileRemovalPreview
{
    private const int FoDelete = 0x0003;
    private const ushort FofAllowUndo = 0x0040;

    private readonly IPlatformEnvironment _platform;
    private readonly ICommandRunner _commands;

    public RecycleBinService()
        : this(new RuntimePlatformEnvironment(), new ProcessCommandRunner())
    {
    }

    public RecycleBinService(IPlatformEnvironment platform, ICommandRunner commands)
    {
        _platform = platform;
        _commands = commands;
    }

    public FileRemovalKind GetRemovalKind()
    {
        if (_platform.IsWindows)
        {
            return FileRemovalKind.RecycleOrTrash;
        }

        if (_platform.IsLinux)
        {
            return _commands.CommandExists("gio") ? FileRemovalKind.RecycleOrTrash : FileRemovalKind.PermanentDelete;
        }

        if (_platform.IsMacOS)
        {
            return _commands.CommandExists("osascript") ? FileRemovalKind.RecycleOrTrash : FileRemovalKind.PermanentDelete;
        }

        return FileRemovalKind.PermanentDelete;
    }

    public async Task MoveToRecycleBinAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
    {
        var paths = filePaths.Where(File.Exists).Distinct(StringComparer.Ordinal).ToArray();
        if (paths.Length == 0)
        {
            return;
        }

        if (_platform.IsWindows)
        {
            await MoveToWindowsRecycleBinAsync(paths, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_platform.IsLinux && _commands.CommandExists("gio"))
        {
            await RunTrashCommandAsync("gio", ["trash", .. paths], cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_platform.IsMacOS && _commands.CommandExists("osascript"))
        {
            foreach (var path in paths)
            {
                var script = $"tell application \"Finder\" to delete POSIX file \"{EscapeAppleScriptString(path)}\"";
                await RunTrashCommandAsync("osascript", ["-e", script], cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        DeletePermanently(paths, cancellationToken);
    }

    private static Task MoveToWindowsRecycleBinAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var from = string.Join('\0', paths) + "\0\0";
            var operation = new ShFileOpStruct
            {
                wFunc = FoDelete,
                pFrom = from,
                fFlags = FofAllowUndo
            };

            var result = SHFileOperation(ref operation);
            if (result != 0)
            {
                throw new InvalidOperationException($"Could not move file(s) to the Recycle Bin. Shell error code: {result}.");
            }
        }, cancellationToken);
    }

    private async Task RunTrashCommandAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var exitCode = await _commands.RunAsync(fileName, arguments, cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} could not move file(s) to the trash. Exit code: {exitCode}.");
        }
    }

    private static void DeletePermanently(IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(path);
        }
    }

    private static string EscapeAppleScriptString(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOpStruct fileOp);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public int wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }
}
