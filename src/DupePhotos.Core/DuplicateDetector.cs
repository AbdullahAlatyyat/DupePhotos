using System.Numerics;

namespace DupePhotos.Core;

public sealed class DuplicateDetector(IImageScanner scanner, IImageHasher hasher) : IDuplicateDetector
{
    private const int LikelyVisualDistance = 8;
    private const int ReviewVisualDistance = 14;
    private const double ReviewVerificationMaxMeanDifference = 18.0;

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
                var distance = HammingDistance(images[left].PerceptualHash, images[right].PerceptualHash);
                if (distance <= LikelyVisualDistance)
                {
                    if (MeanAbsoluteDifference(images[left].VerificationLuma, images[right].VerificationLuma) <= ReviewVerificationMaxMeanDifference)
                    {
                        graph.Connect(left, right, DuplicateMatchKind.LikelyVisualDuplicate, 0.92);
                    }

                    continue;
                }

                if (distance <= ReviewVisualDistance && MeanAbsoluteDifference(images[left].VerificationLuma, images[right].VerificationLuma) <= ReviewVerificationMaxMeanDifference)
                {
                    graph.Connect(left, right, DuplicateMatchKind.NeedsReview, 0.75);
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

    private static double MeanAbsoluteDifference(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
        {
            return double.MaxValue;
        }

        long total = 0;
        for (var i = 0; i < left.Length; i++)
        {
            total += Math.Abs(left[i] - right[i]);
        }

        return total / (double)left.Length;
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
