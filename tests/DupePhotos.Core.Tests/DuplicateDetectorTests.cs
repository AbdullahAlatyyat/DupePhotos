using DupePhotos.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace DupePhotos.Core.Tests;

public sealed class DuplicateDetectorTests
{
    [Fact]
    public async Task FindDuplicatesAsync_GroupsExactDuplicates()
    {
        using var workspace = new TempWorkspace();
        var original = Path.Combine(workspace.Path, "original.png");
        var copy = Path.Combine(workspace.Path, "copy.png");
        var different = Path.Combine(workspace.Path, "different.png");

        await SaveSolidImageAsync(original, Color.Red);
        File.Copy(original, copy);
        await SaveSolidImageAsync(different, Color.Blue);

        var detector = DupePhotosCoreServiceFactory.CreateDuplicateDetector();

        var groups = await detector.FindDuplicatesAsync(workspace.Path);

        var group = Assert.Single(groups);
        Assert.Equal(DuplicateMatchKind.ExactDuplicate, group.MatchKind);
        Assert.Equal(2, group.Items.Count);
        Assert.Contains(group.Items, item => item.Path == original);
        Assert.Contains(group.Items, item => item.Path == copy);
    }

    [Fact]
    public async Task FindDuplicatesAsync_GroupsSamePixelsWithDifferentFileBytes()
    {
        using var workspace = new TempWorkspace();
        var png = Path.Combine(workspace.Path, "image.png");
        var bmp = Path.Combine(workspace.Path, "image.bmp");

        await SaveSolidImageAsync(png, Color.HotPink);
        await SaveSolidImageAsync(bmp, Color.HotPink);

        var detector = DupePhotosCoreServiceFactory.CreateDuplicateDetector();

        var groups = await detector.FindDuplicatesAsync(workspace.Path);

        var group = Assert.Single(groups);
        Assert.Equal(DuplicateMatchKind.SameImageData, group.MatchKind);
        Assert.Equal(2, group.Items.Count);
    }

    [Fact]
    public async Task FindDuplicatesAsync_GroupsRecompressedVisualDuplicates()
    {
        using var workspace = new TempWorkspace();
        var original = Path.Combine(workspace.Path, "gradient.png");
        var resized = Path.Combine(workspace.Path, "gradient-small.jpg");
        var unrelated = Path.Combine(workspace.Path, "unrelated.png");

        await SaveGradientAsync(original, 96, 96);

        using (var image = await Image.LoadAsync<Rgba32>(original))
        {
            image.Mutate(context => context.Resize(80, 80));
            await image.SaveAsJpegAsync(resized);
        }

        await SaveCheckerboardAsync(unrelated);

        var detector = DupePhotosCoreServiceFactory.CreateDuplicateDetector();

        var groups = await detector.FindDuplicatesAsync(workspace.Path);

        var group = Assert.Single(groups);
        Assert.True(group.MatchKind is DuplicateMatchKind.LikelyVisualDuplicate or DuplicateMatchKind.NeedsReview);
        Assert.Contains(group.Items, item => item.Path == original);
        Assert.Contains(group.Items, item => item.Path == resized);
        Assert.DoesNotContain(group.Items, item => item.Path == unrelated);
    }

    private static async Task SaveSolidImageAsync(string path, Color color)
    {
        using var image = new Image<Rgba32>(32, 32, color);
        await image.SaveAsync(path);
    }

    private static async Task SaveGradientAsync(string path, int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgba32((byte)(x * 255 / width), (byte)(y * 255 / height), 140);
                }
            }
        });

        await image.SaveAsPngAsync(path);
    }

    private static async Task SaveCheckerboardAsync(string path)
    {
        using var image = new Image<Rgba32>(96, 96);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = ((x / 8) + (y / 8)) % 2 == 0 ? Color.Black : Color.White;
                }
            }
        });

        await image.SaveAsPngAsync(path);
    }
}
