using DupePhotos.Core;

namespace DupePhotos.Core.Tests;

public sealed class ImageScannerTests
{
    [Fact]
    public async Task EnumerateImages_IncludesSubfoldersAndSupportedExtensions()
    {
        using var workspace = new TempWorkspace();
        var nested = Directory.CreateDirectory(Path.Combine(workspace.Path, "nested"));

        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "root.jpg"), "fake");
        await File.WriteAllTextAsync(Path.Combine(nested.FullName, "child.png"), "fake");
        await File.WriteAllTextAsync(Path.Combine(nested.FullName, "notes.txt"), "ignore");

        var scanner = new ImageScanner();

        var images = scanner.EnumerateImages(workspace.Path, recursive: true);

        Assert.Equal(2, images.Count);
        Assert.All(images, image => Assert.Contains(Path.GetExtension(image.Path), ImageScanner.SupportedExtensions));
    }
}
