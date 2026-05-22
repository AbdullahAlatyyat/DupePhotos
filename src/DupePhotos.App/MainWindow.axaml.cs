using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DupePhotos.App.Services;
using DupePhotos.App.ViewModels;
using DupePhotos.Core;

namespace DupePhotos.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var detector = DupePhotosCoreServiceFactory.CreateDuplicateDetector();
        var recycleBin = new RecycleBinService();
        DataContext = new MainViewModel(detector, recycleBin, PickFolderAsync, ConfirmDeleteAsync);
    }

    private async Task<string?> PickFolderAsync()
    {
        if (!StorageProvider.CanPickFolder)
        {
            return null;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder to scan",
            AllowMultiple = false
        });

        return folders.Count == 0 ? null : folders[0].Path.LocalPath;
    }

    private Task<bool> ConfirmDeleteAsync(IReadOnlyList<string> paths, FileRemovalKind removalKind)
    {
        var action = removalKind == FileRemovalKind.PermanentDelete
            ? "Permanently delete"
            : "Move to trash";
        var message = removalKind == FileRemovalKind.PermanentDelete
            ? "The system trash is not available. Selected files will be permanently deleted."
            : "Selected files will be moved to the system trash or recycle bin.";

        var dialog = new Window
        {
            Title = action,
            Width = 420,
            Height = 190,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        dialog.Content = BuildConfirmationContent(dialog, action, message, paths.Count);

        return dialog.ShowDialog<bool>(this);
    }

    private static Control BuildConfirmationContent(Window dialog, string action, string message, int count)
    {
        var cancel = new Button
        {
            Content = "Cancel",
            MinWidth = 92,
            Margin = new Avalonia.Thickness(0, 0, 8, 0)
        };
        var confirm = new Button
        {
            Content = action,
            MinWidth = 132
        };

        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Children = { cancel, confirm }
        };

        var panel = new StackPanel
        {
            Spacing = 14,
            Margin = new Avalonia.Thickness(18),
            Children =
            {
                new TextBlock { Text = $"{action} {count} file(s)?", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                buttons
            }
        };

        cancel.Click += OnCancelClicked;
        confirm.Click += OnConfirmClicked;
        return panel;

        void OnCancelClicked(object? sender, RoutedEventArgs args)
        {
            dialog.Close(false);
        }

        void OnConfirmClicked(object? sender, RoutedEventArgs args)
        {
            dialog.Close(true);
        }
    }
}
