using Celbridge.Project.ViewModels;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Project.Views;

public sealed partial class ProjectTreeView : UserControl
{
    public ProjectTreeViewModel ViewModel { get; }

    public ProjectTreeView()
    {
        this.InitializeComponent();

        var serviceProvider = ServiceLocator.ServiceProvider;
        ViewModel = serviceProvider.GetRequiredService<ProjectTreeViewModel>();

        ResourcesTreeView.Loaded += ResourcesTreeView_Loaded;
    }

    private void ResourcesTreeView_Loaded(object? sender, RoutedEventArgs e)
    {}

    private void OpenResource(object? sender, RoutedEventArgs e)
    {
        var element = sender as FrameworkElement;
        Guard.IsNotNull(element);
    }

    private void AddResourceToProject(object? sender, RoutedEventArgs e)
    {
        var menuFlyoutItem = sender as MenuFlyoutItem;
        Guard.IsNotNull(menuFlyoutItem);
    }

    private void DeleteResource(object? sender, RoutedEventArgs e)
    {
        var element = sender as FrameworkElement;
        Guard.IsNotNull(element);
    }

    private void DoubleTappedItem(object? sender, DoubleTappedRoutedEventArgs e)
    {
        var element = sender as FrameworkElement;
        Guard.IsNotNull(element);
    }
}
