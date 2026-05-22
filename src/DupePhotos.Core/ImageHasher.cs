using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace DupePhotos.Core;

public sealed class ImageHasher : IImageHasher
{
    private const int PixelHashSize = 32;
    private const int DifferenceHashSize = 8;
    private const int AverageHashSize = 8;
    private const int VerificationSize = 64;

    public async Task<ImageAnalysis> AnalyzeAsync(ImageFileCandidate file, CancellationToken cancellationToken = default)
    {
        var shaTask = ComputeSha256Async(file.Path, cancellationToken);

        using var image = await Image.LoadAsync<Rgba32>(file.Path, cancellationToken).ConfigureAwait(false);

        var pixelHash = ComputePixelHash(image);
        var horizontalDifferenceHash = ComputeHorizontalDifferenceHash(image);
        var verticalDifferenceHash = ComputeVerticalDifferenceHash(image);
        var averageHash = ComputeAverageHash(image);
        var averageColor = ComputeAverageColor(image);
        var verification = BuildVerificationLuma(image);
        var sha256 = await shaTask.ConfigureAwait(false);

        return new ImageAnalysis(
            file.Path,
            file.SizeBytes,
            file.ModifiedAt,
            image.Width,
            image.Height,
            sha256,
            pixelHash,
            horizontalDifferenceHash,
            verticalDifferenceHash,
            averageHash,
            averageColor.Red,
            averageColor.Green,
            averageColor.Blue,
            verification);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static string ComputePixelHash(Image<Rgba32> source)
    {
        using var normalized = source.Clone(context => context.Resize(new ResizeOptions
        {
            Size = new Size(PixelHashSize, PixelHashSize),
            Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.Bicubic
        }));

        using var sha = SHA256.Create();
        var pixelBytes = new byte[4];

        normalized.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    pixelBytes[0] = row[x].R;
                    pixelBytes[1] = row[x].G;
                    pixelBytes[2] = row[x].B;
                    pixelBytes[3] = row[x].A;
                    sha.TransformBlock(pixelBytes, 0, pixelBytes.Length, null, 0);
                }
            }
        });

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }

    private static ulong ComputeHorizontalDifferenceHash(Image<Rgba32> source)
    {
        using var normalized = source.Clone(context => context.Resize(new ResizeOptions
        {
            Size = new Size(DifferenceHashSize + 1, DifferenceHashSize),
            Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.Bicubic
        }));

        var gray = new byte[(DifferenceHashSize + 1) * DifferenceHashSize];
        FillLuma(normalized, gray);

        ulong hash = 0;
        var bit = 0;

        for (var y = 0; y < DifferenceHashSize; y++)
        {
            for (var x = 0; x < DifferenceHashSize; x++)
            {
                var left = gray[y * (DifferenceHashSize + 1) + x];
                var right = gray[y * (DifferenceHashSize + 1) + x + 1];
                if (left > right)
                {
                    hash |= 1UL << bit;
                }

                bit++;
            }
        }

        return hash;
    }

    private static ulong ComputeVerticalDifferenceHash(Image<Rgba32> source)
    {
        using var normalized = source.Clone(context => context.Resize(new ResizeOptions
        {
            Size = new Size(DifferenceHashSize, DifferenceHashSize + 1),
            Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.Bicubic
        }));

        var gray = new byte[DifferenceHashSize * (DifferenceHashSize + 1)];
        FillLuma(normalized, gray);

        ulong hash = 0;
        var bit = 0;

        for (var y = 0; y < DifferenceHashSize; y++)
        {
            for (var x = 0; x < DifferenceHashSize; x++)
            {
                var top = gray[y * DifferenceHashSize + x];
                var bottom = gray[(y + 1) * DifferenceHashSize + x];
                if (top > bottom)
                {
                    hash |= 1UL << bit;
                }

                bit++;
            }
        }

        return hash;
    }

    private static ulong ComputeAverageHash(Image<Rgba32> source)
    {
        using var normalized = source.Clone(context => context.Resize(new ResizeOptions
        {
            Size = new Size(AverageHashSize, AverageHashSize),
            Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.Bicubic
        }));

        var gray = new byte[AverageHashSize * AverageHashSize];
        FillLuma(normalized, gray);
        var average = gray.Average(value => value);

        ulong hash = 0;
        for (var i = 0; i < gray.Length; i++)
        {
            if (gray[i] >= average)
            {
                hash |= 1UL << i;
            }
        }

        return hash;
    }

    private static AverageColor ComputeAverageColor(Image<Rgba32> source)
    {
        long red = 0;
        long green = 0;
        long blue = 0;
        long count = 0;

        source.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    red += row[x].R;
                    green += row[x].G;
                    blue += row[x].B;
                    count++;
                }
            }
        });

        return new AverageColor(
            (byte)(red / count),
            (byte)(green / count),
            (byte)(blue / count));
    }

    private static byte[] BuildVerificationLuma(Image<Rgba32> source)
    {
        using var normalized = source.Clone(context => context.Resize(new ResizeOptions
        {
            Size = new Size(VerificationSize, VerificationSize),
            Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.Bicubic
        }));

        var gray = new byte[VerificationSize * VerificationSize];
        FillLuma(normalized, gray);
        return gray;
    }

    private static void FillLuma(Image<Rgba32> image, byte[] destination)
    {
        image.ProcessPixelRows(accessor =>
        {
            var index = 0;
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    destination[index++] = ToLuma(row[x]);
                }
            }
        });
    }

    private static byte ToLuma(Rgba32 pixel)
    {
        var value = (pixel.R * 299) + (pixel.G * 587) + (pixel.B * 114);
        return (byte)(value / 1000);
    }

    private sealed record AverageColor(byte Red, byte Green, byte Blue);
}
