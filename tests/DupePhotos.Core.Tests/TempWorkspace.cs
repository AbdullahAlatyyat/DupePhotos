namespace DupePhotos.Core.Tests;

internal sealed class TempWorkspace : IDisposable
{
    public TempWorkspace()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dupephotos-{Guid.NewGuid():N}");
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
