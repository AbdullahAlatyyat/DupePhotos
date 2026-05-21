using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace DupePhotos.Core;

public sealed class ImageHasher : IImageHasher
{
    private const int PixelHashSize = 32;
    private const int DHashWidth = 9;
    private const int DHashHeight = 8;
    private const int VerificationSize = 64;

    public async Task<ImageAnalysis> AnalyzeAsync(ImageFileCandidate file, CancellationToken cancellationToken = default)
    {
        var shaTask = ComputeSha256Async(file.Path, cancellationToken);

        using var image = await Image.LoadAsync<Rgba32>(file.Path, cancellationToken).ConfigureAwait(false);

        var pixelHash = ComputePixelHash(image);
        var perceptualHash = ComputeDHash(image);
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
            perceptualHash,
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

    private static ulong ComputeDHash(Image<Rgba32> source)
    {
        using var normalized = source.Clone(context => context.Resize(new ResizeOptions
        {
            Size = new Size(DHashWidth, DHashHeight),
            Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.Bicubic
        }));

        var gray = new byte[DHashWidth * DHashHeight];
        FillLuma(normalized, gray);

        ulong hash = 0;
        var bit = 0;

        for (var y = 0; y < DHashHeight; y++)
        {
            for (var x = 0; x < DHashWidth - 1; x++)
            {
                var left = gray[y * DHashWidth + x];
                var right = gray[y * DHashWidth + x + 1];
                if (left > right)
                {
                    hash |= 1UL << bit;
                }

                bit++;
            }
        }

        return hash;
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
}
