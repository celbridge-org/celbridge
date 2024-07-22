using Celbridge.Projects.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.Projects.Views;

public sealed partial class ProjectPanel : UserControl
{
    private IStringLocalizer _stringLocalizer;
    private LocalizedString RefreshTooltipString => _stringLocalizer.GetString("ProjectPanel_RefreshTooltip");

    public ProjectPanelViewModel ViewModel { get; }

    public ProjectPanel()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;

        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();
        ViewModel = serviceProvider.GetRequiredService<ProjectPanelViewModel>();

        var refreshProjectButton = new Button()
            .Grid(column: 2)
            .Content(new SymbolIcon(Symbol.Refresh))
            .Command(ViewModel.RefreshResourceTreeCommand)
            .Margin(0, 0, 8, 0)
            .VerticalAlignment(VerticalAlignment.Center);

        ToolTipService.SetToolTip(refreshProjectButton, RefreshTooltipString);
        ToolTipService.SetPlacement(refreshProjectButton, PlacementMode.Bottom);

        var titleBar = new Grid()
            .Background(ThemeResource.Get<Brush>("PanelBackgroundBrush"))
            .BorderBrush(ThemeResource.Get<Brush>("PanelBorderBrush"))
            .BorderThickness(0, 0, 0, 1)
            .ColumnDefinitions("Auto, *, Auto, 44")
            .Children(
                new TextBlock()
                    .Grid(column: 0)
                    .Text(x => x.Bind(() => ViewModel.TitleText).Mode(BindingMode.OneWay))
                    .Margin(48, 0, 0, 0)
                    .VerticalAlignment(VerticalAlignment.Center),
                refreshProjectButton
            );

        var resourceTreeView = new ResourceTreeView()
            .Grid(row:1);

        var panelGrid = new Grid()
            .RowDefinitions("40, *")
            .VerticalAlignment(VerticalAlignment.Stretch)
            .Children(titleBar, resourceTreeView);
           
        //
        // Set the data context and page content
        // 

        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(panelGrid));
    }
}