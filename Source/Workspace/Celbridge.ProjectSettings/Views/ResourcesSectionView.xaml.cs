using Celbridge.ProjectSettings.ViewModels;

namespace Celbridge.ProjectSettings.Views;

/// <summary>
/// The Resources section of the Project Settings. It edits the patterns that the Explorer hides and the
/// patterns that search skips, each as a block of glob patterns, one per line. It also edits the folder that
/// downloads are saved to, and the package folders.
/// </summary>
public sealed partial class ResourcesSectionView : UserControl
{
    private ResourcesSectionViewModel? _viewModel;

    // Supplied by the panel that owns this section. Assigning it refreshes the bindings so the section
    // populates once the panel hands over its instance.
    public ResourcesSectionViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value;
            PackageFolderList.ItemsSource = value?.PackageFolders;
            Bindings?.Update();
        }
    }

    public ResourcesSectionView()
    {
        InitializeComponent();
    }

    private void BrowseDownloadsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        _ = ViewModel?.PickDownloadsFolderAsync();
    }

    private void AddPackageFolderButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.AddPackageFolder();
    }

    private void BrowsePackageFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var browseButton = (FrameworkElement)sender;
        if (browseButton.DataContext is not PackageFolderViewModel packageFolder
            || ViewModel is null)
        {
            return;
        }

        _ = ViewModel.PickPackageFolderAsync(packageFolder);
    }

    private void RemovePackageFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var removeButton = (FrameworkElement)sender;
        if (removeButton.DataContext is not PackageFolderViewModel packageFolder)
        {
            return;
        }

        ViewModel?.RemovePackageFolder(packageFolder);
    }
}
