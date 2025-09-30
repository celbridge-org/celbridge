using Celbridge.Explorer.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Views;

public sealed partial class ExplorerPanel : UserControl, IExplorerPanel
{
    private IStringLocalizer _stringLocalizer;
    private LocalizedString RefreshTooltipString => _stringLocalizer.GetString("ExplorerPanel_RefreshTooltip");

    public ExplorerPanelViewModel ViewModel { get; }

    private readonly ResourceTreeView _resourceTreeView;

    public ExplorerPanel()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<ExplorerPanelViewModel>();

        var refreshProjectButton = new Button()
            .Grid(column: 2)
            .Content(new SymbolIcon(Symbol.Refresh))
            .Command(ViewModel.RefreshResourceTreeCommand)
            .Margin(0, 0, 2, 0)
            .VerticalAlignment(VerticalAlignment.Center);

        ToolTipService.SetToolTip(refreshProjectButton, RefreshTooltipString);
        ToolTipService.SetPlacement(refreshProjectButton, PlacementMode.Bottom);

        var titleBar = new Grid()
            .Background(ThemeResource.Get<Brush>("PanelBackgroundBrush"))
            .BorderBrush(ThemeResource.Get<Brush>("PanelBorderBrush"))
            .BorderThickness(0, 0, 0, 1)
            .ColumnDefinitions("Auto, *, Auto, 46")
            .Children(
                new TextBlock()
                    .Grid(column: 0)
                    .Text(x => x.Binding(() => ViewModel.TitleText).Mode(BindingMode.OneWay))
                    .Margin(48, 0, 0, 0)
                    .VerticalAlignment(VerticalAlignment.Center),
                refreshProjectButton
            );

        _resourceTreeView = new ResourceTreeView()
            .Grid(row:1);

        var panelGrid = new Grid()
            .RowDefinitions("40, *")
            .VerticalAlignment(VerticalAlignment.Stretch)
            .Children(titleBar, _resourceTreeView);
           
        //
        // Set the data context and page content
        // 

        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(panelGrid));
    }

    public ResourceKey GetSelectedResource()
    {
        return _resourceTreeView.GetSelectedResource();
    }

    public async Task<Result> SelectResource(ResourceKey resource)
    {
        return await _resourceTreeView.SetSelectedResource(resource);
    }
}
