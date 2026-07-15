using Celbridge.Explorer.ViewModels;
using Celbridge.UserInterface;

namespace Celbridge.Explorer.Views;

public sealed partial class ExplorerPanel : UserControl, IExplorerPanel
{
    public ExplorerPanelViewModel ViewModel { get; }

    public ExplorerPanel()
    {
        ViewModel = ServiceLocator.AcquireService<ExplorerPanelViewModel>();

        InitializeComponent();

        FocusTracking.SetEditTarget(this, ResourceTree);
    }

    public void FocusPanel()
    {
        ResourceTree.FocusTree();
    }

    public List<ResourceKey> GetSelectedResources()
    {
        return ResourceTree.GetSelectedResources();
    }

    private void PanelHeader_Tapped(object sender, TappedRoutedEventArgs e)
    {
        FocusPanel();
    }

    public async Task<Result> SelectResources(List<ResourceKey> resources)
    {
        return await ResourceTree.SelectResources(resources);
    }

    private void ExplorerToolbar_NewFileClicked(object sender, EventArgs e)
    {
        ResourceTree.NewFileToSelectedFolder();
    }

    private void ExplorerToolbar_NewFolderClicked(object sender, EventArgs e)
    {
        ResourceTree.NewFolderToSelectedFolder();
    }

    private void ExplorerToolbar_CollapseFoldersClicked(object sender, EventArgs e)
    {
        ResourceTree.CollapseAllFolders();
    }
}
