using System.Runtime.InteropServices;
using DupePhotos.Core;

namespace DupePhotos.App.Services;

public sealed class RecycleBinService : IRecycleBinService
{
    private const int FoDelete = 0x0003;
    private const ushort FofAllowUndo = 0x0040;

    public Task MoveToRecycleBinAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
    {
        var paths = filePaths.Where(File.Exists).ToArray();
        if (paths.Length == 0)
        {
            return Task.CompletedTask;
        }

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
