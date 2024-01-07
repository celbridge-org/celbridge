using CommunityToolkit.Mvvm.Messaging;

namespace CelLegacy.Views;

public sealed partial class ProjectPanel : UserControl
{
    private readonly IMessenger _messengerService;
    private readonly IInspectorService _inspectorService;
    private readonly IDocumentService _documentService;

    public ProjectViewModel ViewModel { get; }

    public ProjectPanel()
    {
        this.InitializeComponent();

        var services = LegacyServiceProvider.Services!;
        ViewModel = services.GetRequiredService<ProjectViewModel>();
        _messengerService = services.GetRequiredService<IMessenger>();
        _inspectorService = services.GetRequiredService<IInspectorService>();
        _documentService = services.GetRequiredService<IDocumentService>();

        _messengerService.Register<SelectedEntityChangedMessage>(this, OnSelectedEntityChanged);

        ResourcesTreeView.Loaded += ResourcesTreeView_Loaded;
    }

    private void ResourcesTreeView_Loaded(object? sender, RoutedEventArgs e)
    {
        var selectedEntity = _inspectorService.SelectedEntity;
        UpdateSelectedEntity(selectedEntity);
    }

    private void OnSelectedEntityChanged(object r, SelectedEntityChangedMessage m)
    {
        var selectedEntity = m.Entity;
        UpdateSelectedEntity(selectedEntity);
    }

    private void UpdateSelectedEntity(IEntity? selectedEntity)
    {
        if (selectedEntity is Resource)
        {
            // Set this resource as the active item in the tree
            // Note: Using TreeView.SelectedItem worked ok, but for some reason it wouldn't
            // select items at the root level. Using TreeViewNodes instead works fine.
            var matchingNode = FindMatchingNode(ResourcesTreeView.RootNodes, selectedEntity);
            if (matchingNode != null)
            {
                if (ResourcesTreeView.SelectedNode != matchingNode)
                {
                    ResourcesTreeView.SelectedNode = matchingNode;
                }
            }
        }
        else
        {
            ResourcesTreeView.SelectedItem = null;
        }
    }

    private TreeViewNode? FindMatchingNode(IEnumerable<TreeViewNode> nodes, IEntity searchEntity)
    {
        foreach (TreeViewNode node in nodes)
        {
            // If this node's DataContext matches the search object, return this node
            if (node.Content == searchEntity)
            {
                return node;
            }

            // If this node has children, recursively search through them
            if (node.Children != null && node.Children.Any())
            {
                var matchingChildNode = FindMatchingNode(node.Children, searchEntity);

                // If we found a matching node in this child's subtree, return it
                if (matchingChildNode is not null)
                {
                    return matchingChildNode;
                }
            }
        }

        // If no matching node was found, return null
        return null;
    }

    private void OpenResource(object? sender, RoutedEventArgs e)
    {
        var element = sender as FrameworkElement;
        Guard.IsNotNull(element);

        OpenDocument(element);
    }

    private void AddResourceToProject(object? sender, RoutedEventArgs e)
    {
        var menuFlyoutItem = sender as MenuFlyoutItem;
        Guard.IsNotNull(menuFlyoutItem);

        if (menuFlyoutItem.DataContext is FolderResource folderResource)
        {
            _inspectorService.SelectedEntity = folderResource as IEntity;
        }
        else if(menuFlyoutItem.DataContext is ProjectViewModel)
        {
            // If the data context is the project, add the resource in the root folder
            _inspectorService.SelectedEntity = null;
        }
        else
        {
            throw new NotImplementedException();
        }

        // The OnSelectedEntityChanged message handler above ensures that the
        // corresponding TreeView node is selected.

        ViewModel.AddResourceCommand.ExecuteAsync(null);
    }

    private void DeleteResource(object? sender, RoutedEventArgs e)
    {
        var element = sender as FrameworkElement;
        Guard.IsNotNull(element);

        var entity = element.DataContext as Entity;
        Guard.IsNotNull(entity);

        ViewModel.DeleteEntity(entity);
    }

    private void DoubleTappedItem(object? sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        var element = sender as FrameworkElement;
        Guard.IsNotNull(element);

        OpenDocument(element);
    }

    private void OpenProjectFolder(object? sender, RoutedEventArgs e)
    {
        ViewModel.OpenProjectFolderCommand.Execute(null);
    }

    private void OpenDocument(FrameworkElement element)
    {
        var resource = element.DataContext as Resource;
        Guard.IsNotNull(resource);

        var documentEntity = resource as IDocumentEntity;
        Guard.IsNotNull(documentEntity);

        var result = _documentService.OpenDocument(documentEntity);
        if (result is ErrorResult error)
        {
            Log.Error(error.Message);
        }
    }
}
