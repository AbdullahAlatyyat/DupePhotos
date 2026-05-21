using System.Collections.ObjectModel;
using DupePhotos.Core;

namespace DupePhotos.App.ViewModels;

public sealed class DuplicateGroupViewModel
{
    public DuplicateGroupViewModel(DuplicateGroup group)
    {
        MatchKind = group.MatchKind;
        Confidence = group.Confidence;
        Items = new ObservableCollection<DuplicateItemViewModel>(group.Items.Select(item => new DuplicateItemViewModel(item)));
    }

    public DuplicateMatchKind MatchKind { get; }

    public double Confidence { get; }

    public ObservableCollection<DuplicateItemViewModel> Items { get; }

    public string Title => MatchKind switch
    {
        DuplicateMatchKind.ExactDuplicate => "Exact duplicate",
        DuplicateMatchKind.SameImageData => "Same image data",
        DuplicateMatchKind.LikelyVisualDuplicate => "Likely visual duplicate",
        DuplicateMatchKind.NeedsReview => "Needs review",
        _ => "Duplicate group"
    };

    public string Subtitle => $"{Items.Count} images in this group";

    public string ConfidenceText => $"{Confidence:P0} confidence";
}
