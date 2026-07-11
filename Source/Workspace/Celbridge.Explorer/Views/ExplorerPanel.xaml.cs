using Celbridge.Explorer.ViewModels;
using Celbridge.UserInterface;

namespace Celbridge.Explorer.Views;

public sealed partial class ExplorerPanel : UserControl, IExplorerPanel
{
    private bool _isPointerOver;
    private bool _hasFocus;
    private bool _isToolbarRevealed;

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

    public void SetToolbarRevealed(bool revealed)
    {
        _isToolbarRevealed = revealed;
        UpdateToolbarVisibility();
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
        // Show toolbar when panel has focus, the mouse pointer is over it, or a spotlight is
        // revealing one of its buttons
        var isToolbarVisible = _hasFocus || _isPointerOver || _isToolbarRevealed;
        ExplorerToolbar.SetToolbarVisible(isToolbarVisible);
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
