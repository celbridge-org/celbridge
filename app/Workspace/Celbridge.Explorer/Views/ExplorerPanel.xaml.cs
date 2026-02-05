using Celbridge.Explorer.ViewModels;

namespace Celbridge.Explorer.Views;

public sealed partial class ExplorerPanel : UserControl, IExplorerPanel
{
    private bool _isPointerOver;
    private bool _hasFocus;

    public ExplorerPanelViewModel ViewModel { get; }

    public ExplorerPanel()
    {
        ViewModel = ServiceLocator.AcquireService<ExplorerPanelViewModel>();

        InitializeComponent();
    }

    public ResourceKey GetSelectedResource()
    {
        return ResourceView.GetSelectedResource();
    }

    public List<ResourceKey> GetSelectedResources()
    {
        return ResourceView.GetSelectedResources();
    }

    public async Task<Result> SelectResource(ResourceKey resource)
    {
        return await ResourceView.SelectResource(resource);
    }

    public async Task<Result> SelectResources(List<ResourceKey> resources)
    {
        return await ResourceView.SelectResources(resources);
    }

    private void UserControl_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = true;
        UpdateToolbarVisibility();
    }

    private void UserControl_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = false;
        UpdateToolbarVisibility();
    }

    private void UserControl_GotFocus(object sender, RoutedEventArgs e)
    {
        _hasFocus = true;
        UpdateToolbarVisibility();
    }

    private void UserControl_LostFocus(object sender, RoutedEventArgs e)
    {
        _hasFocus = false;
        UpdateToolbarVisibility();
    }

    private void UpdateToolbarVisibility()
    {
        // Show toolbar when panel has focus or mouse pointer is over it
        var isToolbarVisible = _hasFocus || _isPointerOver;
        ExplorerToolbar.SetToolbarVisible(isToolbarVisible);
    }

    private void ExplorerToolbar_AddFileClicked(object sender, EventArgs e)
    {
        ResourceView.AddFileToSelectedFolder();
    }

    private void ExplorerToolbar_AddFolderClicked(object sender, EventArgs e)
    {
        ResourceView.AddFolderToSelectedFolder();
    }

    private void ExplorerToolbar_CollapseFoldersClicked(object sender, EventArgs e)
    {
        ResourceView.CollapseAllFolders();
    }
}
