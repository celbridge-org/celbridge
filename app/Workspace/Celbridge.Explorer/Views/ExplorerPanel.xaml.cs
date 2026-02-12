using Celbridge.Explorer.ViewModels;
using Celbridge.UserInterface;

namespace Celbridge.Explorer.Views;

public sealed partial class ExplorerPanel : UserControl, IExplorerPanel
{
    private readonly IPanelFocusService _panelFocusService;
    private bool _isPointerOver;
    private bool _hasFocus;

    public ExplorerPanelViewModel ViewModel { get; }

    public ExplorerPanel()
    {
        _panelFocusService = ServiceLocator.AcquireService<IPanelFocusService>();
        ViewModel = ServiceLocator.AcquireService<ExplorerPanelViewModel>();

        InitializeComponent();
    }

    public List<ResourceKey> GetSelectedResources()
    {
        return ResourceTree.GetSelectedResources();
    }

    public async Task<Result> SelectResources(List<ResourceKey> resources)
    {
        return await ResourceTree.SelectResources(resources);
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

    private void UserControl_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _panelFocusService.SetFocusedPanel(FocusablePanel.Explorer);
    }

    private void UserControl_GotFocus(object sender, RoutedEventArgs e)
    {
        _hasFocus = true;
        _panelFocusService.SetFocusedPanel(FocusablePanel.Explorer);
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
        ResourceTree.AddFileToSelectedFolder();
    }

    private void ExplorerToolbar_AddFolderClicked(object sender, EventArgs e)
    {
        ResourceTree.AddFolderToSelectedFolder();
    }

    private void ExplorerToolbar_CollapseFoldersClicked(object sender, EventArgs e)
    {
        ResourceTree.CollapseAllFolders();
    }
}
