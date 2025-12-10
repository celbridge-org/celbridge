using Celbridge.Explorer.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Views;

public sealed partial class ExplorerPanel : UserControl, IExplorerPanel
{
    private readonly IStringLocalizer _stringLocalizer;

    public ExplorerPanelViewModel ViewModel { get; }

    public LocalizedString RefreshTooltipString => _stringLocalizer.GetString("ExplorerPanel_RefreshTooltip");

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
}
