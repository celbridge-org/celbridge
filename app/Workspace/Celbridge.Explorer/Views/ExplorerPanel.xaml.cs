using Celbridge.Explorer.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Views;

public sealed partial class ExplorerPanel : UserControl, IExplorerPanel
{
    private readonly IStringLocalizer _stringLocalizer;

    private bool _isPointerOver;
    private bool _hasFocus;

    public ExplorerPanelViewModel ViewModel { get; }

    public ExplorerPanel()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<ExplorerPanelViewModel>();

        InitializeComponent();
    }

    public ResourceKey GetSelectedResource()
    {
        return ResourceTreeView.GetSelectedResource();
    }

    public async Task<Result> SelectResource(ResourceKey resource)
    {
        return await ResourceTreeView.SetSelectedResource(resource);
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
        ResourceTreeView.SetToolbarVisible(isToolbarVisible);
    }
}
