using System.Numerics;

namespace DupePhotos.Core;

public sealed class DuplicateDetector(IImageScanner scanner, IImageHasher hasher) : IDuplicateDetector
{
    private const double AspectRatioTolerance = 0.015;
    private const int LikelyDifferenceHashDistance = 8;
    private const int LikelyAverageHashDistance = 4;
    private const double LikelyMaxMeanDifference = 0.045;
    private const double LikelyMaxRootMeanSquareDifference = 0.06;
    private const double LikelyMinShapeSimilarity = 0.985;
    private const double LikelyMaxAverageColorDistance = 10.0;
    private const int ReviewDifferenceHashDistance = 16;
    private const int ReviewAverageHashDistance = 8;
    private const double ReviewMaxMeanDifference = 0.075;
    private const double ReviewMaxRootMeanSquareDifference = 0.10;
    private const double ReviewMinShapeSimilarity = 0.96;
    private const double ReviewMaxAverageColorDistance = 20.0;

    public async Task<IReadOnlyList<DuplicateGroup>> FindDuplicatesAsync(
        string folderPath,
        bool recursive = true,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var files = scanner.EnumerateImages(folderPath, recursive);
        var analyses = new List<ImageAnalysis>();

        progress?.Report(new ScanProgress(files.Count, 0, 0, null));

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                analyses.Add(await hasher.AnalyzeAsync(file, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Unsupported or damaged images are skipped so one bad file does not stop a library scan.
            }

            progress?.Report(new ScanProgress(files.Count, analyses.Count, 0, file.Path));
        }

        var groups = BuildGroups(analyses);
        progress?.Report(new ScanProgress(files.Count, analyses.Count, groups.Count, null));
        return groups;
    }

    private static IReadOnlyList<DuplicateGroup> BuildGroups(IReadOnlyList<ImageAnalysis> images)
    {
        if (images.Count < 2)
        {
            return [];
        }

        var imageIndexes = images
            .Select((image, index) => new { image.Path, Index = index })
            .ToDictionary(item => item.Path, item => item.Index, StringComparer.OrdinalIgnoreCase);
        var graph = new MatchGraph(images.Count);

        foreach (var sizeGroup in images.GroupBy(image => image.SizeBytes).Where(group => group.Count() > 1))
        {
            foreach (var shaGroup in sizeGroup.GroupBy(image => image.Sha256).Where(group => group.Count() > 1))
            {
                ConnectAll(shaGroup.Select(image => imageIndexes[image.Path]), graph, DuplicateMatchKind.ExactDuplicate, 1.0);
            }
        }

        foreach (var pixelGroup in images.GroupBy(image => image.PixelHash).Where(group => group.Count() > 1))
        {
            ConnectAll(pixelGroup.Select(image => imageIndexes[image.Path]), graph, DuplicateMatchKind.SameImageData, 0.99);
        }

        for (var left = 0; left < images.Count; left++)
        {
            for (var right = left + 1; right < images.Count; right++)
            {
                var visualMatch = CompareVisualSimilarity(images[left], images[right]);
                if (visualMatch.Kind is not null)
                {
                    graph.Connect(left, right, visualMatch.Kind.Value, visualMatch.Confidence);
                }
            }
        }

        return graph.Components()
            .Where(component => component.Indexes.Count > 1)
            .Select((component, index) => ToDuplicateGroup($"group-{index + 1}", component, images))
            .OrderByDescending(group => group.Items.Count)
            .ThenBy(group => group.MatchKind)
            .ToList();
    }

    private static void ConnectAll(IEnumerable<int> indexes, MatchGraph graph, DuplicateMatchKind kind, double confidence)
    {
        var list = indexes.ToList();
        for (var i = 1; i < list.Count; i++)
        {
            graph.Connect(list[0], list[i], kind, confidence);
        }
    }

    private static DuplicateGroup ToDuplicateGroup(string id, MatchComponent component, IReadOnlyList<ImageAnalysis> images)
    {
        var strongestKind = component.StrongestKind;
        var confidence = component.Confidence;
        var keepPath = component.Indexes
            .Select(index => images[index])
            .OrderByDescending(image => image.Width * image.Height)
            .ThenByDescending(image => image.SizeBytes)
            .ThenByDescending(image => image.ModifiedAt)
            .First()
            .Path;

        var items = component.Indexes
            .Select(index => images[index])
            .OrderByDescending(image => image.Path == keepPath)
            .ThenBy(image => image.Path, StringComparer.OrdinalIgnoreCase)
            .Select(image => new DuplicateImageItem(
                image.Path,
                image.SizeBytes,
                image.ModifiedAt,
                image.Width,
                image.Height,
                image.Path == keepPath))
            .ToList();

        return new DuplicateGroup(id, strongestKind, confidence, items);
    }

    private static int HammingDistance(ulong left, ulong right)
    {
        return BitOperations.PopCount(left ^ right);
    }

    private static VisualMatch CompareVisualSimilarity(ImageAnalysis left, ImageAnalysis right)
    {
        if (!HasSimilarAspectRatio(left, right))
        {
            return VisualMatch.None;
        }

        var horizontalDistance = HammingDistance(left.HorizontalDifferenceHash, right.HorizontalDifferenceHash);
        var verticalDistance = HammingDistance(left.VerticalDifferenceHash, right.VerticalDifferenceHash);
        var averageDistance = HammingDistance(left.AverageHash, right.AverageHash);
        var differenceDistance = horizontalDistance + verticalDistance;
        var averageColorDistance = AverageColorDistance(left, right);
        var verification = CompareVerificationLuma(left.VerificationLuma, right.VerificationLuma);

        if (differenceDistance <= LikelyDifferenceHashDistance
            && averageDistance <= LikelyAverageHashDistance
            && averageColorDistance <= LikelyMaxAverageColorDistance
            && verification.MeanAbsoluteDifference <= LikelyMaxMeanDifference
            && verification.RootMeanSquareDifference <= LikelyMaxRootMeanSquareDifference
            && verification.ShapeSimilarity >= LikelyMinShapeSimilarity)
        {
            return new VisualMatch(DuplicateMatchKind.LikelyVisualDuplicate, 0.92);
        }

        if (differenceDistance <= ReviewDifferenceHashDistance
            && averageDistance <= ReviewAverageHashDistance
            && averageColorDistance <= ReviewMaxAverageColorDistance
            && verification.MeanAbsoluteDifference <= ReviewMaxMeanDifference
            && verification.RootMeanSquareDifference <= ReviewMaxRootMeanSquareDifference
            && verification.ShapeSimilarity >= ReviewMinShapeSimilarity)
        {
            return new VisualMatch(DuplicateMatchKind.NeedsReview, 0.75);
        }

        return VisualMatch.None;
    }

    private static bool HasSimilarAspectRatio(ImageAnalysis left, ImageAnalysis right)
    {
        var leftRatio = left.Width / (double)left.Height;
        var rightRatio = right.Width / (double)right.Height;
        return Math.Abs(leftRatio - rightRatio) / Math.Max(leftRatio, rightRatio) <= AspectRatioTolerance;
    }

    private static double AverageColorDistance(ImageAnalysis left, ImageAnalysis right)
    {
        var red = left.AverageRed - right.AverageRed;
        var green = left.AverageGreen - right.AverageGreen;
        var blue = left.AverageBlue - right.AverageBlue;
        return Math.Sqrt((red * red) + (green * green) + (blue * blue));
    }

    private static VerificationComparison CompareVerificationLuma(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
        {
            return VerificationComparison.Mismatch;
        }

        long total = 0;
        long squaredTotal = 0;
        long leftTotal = 0;
        long rightTotal = 0;
        for (var i = 0; i < left.Length; i++)
        {
            var difference = left[i] - right[i];
            total += Math.Abs(difference);
            squaredTotal += difference * difference;
            leftTotal += left[i];
            rightTotal += right[i];
        }

        var leftMean = leftTotal / (double)left.Length;
        var rightMean = rightTotal / (double)right.Length;
        double covariance = 0;
        double leftVariance = 0;
        double rightVariance = 0;

        for (var i = 0; i < left.Length; i++)
        {
            var leftDelta = left[i] - leftMean;
            var rightDelta = right[i] - rightMean;
            covariance += leftDelta * rightDelta;
            leftVariance += leftDelta * leftDelta;
            rightVariance += rightDelta * rightDelta;
        }

        var shapeSimilarity = leftVariance == 0 || rightVariance == 0
            ? total == 0 ? 1.0 : 0.0
            : covariance / Math.Sqrt(leftVariance * rightVariance);

        return new VerificationComparison(
            total / (double)(left.Length * byte.MaxValue),
            Math.Sqrt(squaredTotal / (double)left.Length) / byte.MaxValue,
            shapeSimilarity);
    }

    private readonly record struct VisualMatch(DuplicateMatchKind? Kind, double Confidence)
    {
        public static VisualMatch None { get; } = new(null, 0);
    }

    private readonly record struct VerificationComparison(
        double MeanAbsoluteDifference,
        double RootMeanSquareDifference,
        double ShapeSimilarity)
    {
        public static VerificationComparison Mismatch { get; } = new(double.MaxValue, double.MaxValue, double.MinValue);
    }

    private sealed class MatchGraph(int count)
    {
        private readonly int[] _parents = Enumerable.Range(0, count).ToArray();
        private readonly Dictionary<int, DuplicateMatchKind> _strongestKinds = [];
        private readonly Dictionary<int, double> _confidences = [];

        public void Connect(int left, int right, DuplicateMatchKind kind, double confidence)
        {
            var root = Union(left, right);

            if (!_strongestKinds.TryGetValue(root, out var existing) || kind < existing)
            {
                _strongestKinds[root] = kind;
            }

            _confidences[root] = Math.Max(_confidences.GetValueOrDefault(root), confidence);
        }

        public IEnumerable<MatchComponent> Components()
        {
            var components = new Dictionary<int, List<int>>();
            for (var i = 0; i < _parents.Length; i++)
            {
                var root = Find(i);
                if (!components.TryGetValue(root, out var indexes))
                {
                    indexes = [];
                    components[root] = indexes;
                }

                indexes.Add(i);
            }

            foreach (var (root, indexes) in components)
            {
                yield return new MatchComponent(
                    indexes,
                    _strongestKinds.GetValueOrDefault(root, DuplicateMatchKind.NeedsReview),
                    _confidences.GetValueOrDefault(root));
            }
        }

        private int Union(int left, int right)
        {
            var leftRoot = Find(left);
            var rightRoot = Find(right);

            if (leftRoot == rightRoot)
            {
                return leftRoot;
            }

            _parents[rightRoot] = leftRoot;
            MergeMetadata(leftRoot, rightRoot);
            return leftRoot;
        }

        private int Find(int index)
        {
            if (_parents[index] == index)
            {
                return index;
            }

            _parents[index] = Find(_parents[index]);
            return _parents[index];
        }

        private void MergeMetadata(int root, int oldRoot)
        {
            if (_strongestKinds.TryGetValue(oldRoot, out var oldKind)
                && (!_strongestKinds.TryGetValue(root, out var kind) || oldKind < kind))
            {
                _strongestKinds[root] = oldKind;
            }

            if (_confidences.TryGetValue(oldRoot, out var oldConfidence))
            {
                _confidences[root] = Math.Max(_confidences.GetValueOrDefault(root), oldConfidence);
            }
        }
    }

    private sealed record MatchComponent(
        IReadOnlyList<int> Indexes,
        DuplicateMatchKind StrongestKind,
        double Confidence);
}
