using Celbridge.Project.ViewModels;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Project.Views;

public sealed partial class ResourceTreeView : UserControl
{
    public ResourceTreeViewModel ViewModel { get; }

    public ResourceTreeView()
    {
        this.InitializeComponent();

        var serviceProvider = ServiceLocator.ServiceProvider;
        ViewModel = serviceProvider.GetRequiredService<ResourceTreeViewModel>();

        Unloaded += (s,e) =>
        {
            ViewModel.ResourceTreeView_Unloaded();
        };
    }

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
