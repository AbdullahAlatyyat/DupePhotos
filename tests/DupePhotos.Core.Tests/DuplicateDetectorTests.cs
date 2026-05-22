using DupePhotos.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
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

    [Fact]
    public async Task FindDuplicatesAsync_GroupsLowQualityJpegFromSameImage()
    {
        using var workspace = new TempWorkspace();
        var original = Path.Combine(workspace.Path, "gradient.png");
        var jpeg = Path.Combine(workspace.Path, "gradient-low-quality.jpg");

        await SaveGradientAsync(original, 128, 96);

        using (var image = await Image.LoadAsync<Rgba32>(original))
        {
            await image.SaveAsJpegAsync(jpeg, new JpegEncoder { Quality = 60 });
        }

        var detector = DupePhotosCoreServiceFactory.CreateDuplicateDetector();

        var groups = await detector.FindDuplicatesAsync(workspace.Path);

        var group = Assert.Single(groups);
        Assert.True(group.MatchKind is DuplicateMatchKind.LikelyVisualDuplicate or DuplicateMatchKind.NeedsReview);
        Assert.Contains(group.Items, item => item.Path == original);
        Assert.Contains(group.Items, item => item.Path == jpeg);
    }

    [Fact]
    public async Task FindDuplicatesAsync_DoesNotGroupSimilarButDifferentGradients()
    {
        using var workspace = new TempWorkspace();
        var first = Path.Combine(workspace.Path, "first.png");
        var second = Path.Combine(workspace.Path, "second.png");

        await SaveGradientAsync(first, 96, 96);
        await SaveDifferentGradientAsync(second, 96, 96);

        var detector = DupePhotosCoreServiceFactory.CreateDuplicateDetector();

        var groups = await detector.FindDuplicatesAsync(workspace.Path);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task FindDuplicatesAsync_DoesNotGroupSameSizeUnrelatedImages()
    {
        using var workspace = new TempWorkspace();
        var gradient = Path.Combine(workspace.Path, "gradient.png");
        var checkerboard = Path.Combine(workspace.Path, "checkerboard.png");

        await SaveGradientAsync(gradient, 96, 96);
        await SaveCheckerboardAsync(checkerboard);

        var detector = DupePhotosCoreServiceFactory.CreateDuplicateDetector();

        var groups = await detector.FindDuplicatesAsync(workspace.Path);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task FindDuplicatesAsync_DoesNotGroupDifferentFlatColorsWithSimilarBrightness()
    {
        using var workspace = new TempWorkspace();
        var red = Path.Combine(workspace.Path, "red.png");
        var green = Path.Combine(workspace.Path, "green.png");

        await SaveSolidImageAsync(red, Color.Red);
        await SaveSolidImageAsync(green, new Color(new Rgba32(0, 130, 0)));

        var detector = DupePhotosCoreServiceFactory.CreateDuplicateDetector();

        var groups = await detector.FindDuplicatesAsync(workspace.Path);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task FindDuplicatesAsync_DoesNotGroupCroppedImages()
    {
        using var workspace = new TempWorkspace();
        var original = Path.Combine(workspace.Path, "gradient.png");
        var cropped = Path.Combine(workspace.Path, "gradient-cropped.png");

        await SaveGradientAsync(original, 96, 96);

        using (var image = await Image.LoadAsync<Rgba32>(original))
        {
            image.Mutate(context => context.Crop(new Rectangle(8, 0, 80, 96)));
            await image.SaveAsPngAsync(cropped);
        }

        var detector = DupePhotosCoreServiceFactory.CreateDuplicateDetector();

        var groups = await detector.FindDuplicatesAsync(workspace.Path);

        Assert.Empty(groups);
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

    private static async Task SaveDifferentGradientAsync(string path, int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgba32(
                        (byte)(255 - (x * 255 / width)),
                        (byte)(y * 255 / height),
                        (byte)(x * 255 / width));
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
