using DupePhotos.App.Services;
using DupePhotos.App.ViewModels;
using DupePhotos.Core;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DupePhotos.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var detector = DupePhotosCoreServiceFactory.CreateDuplicateDetector();
        var recycleBin = new RecycleBinService();
        var viewModel = new MainViewModel(detector, recycleBin, PickFolderAsync);

        DataContext = viewModel;
    }

    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
