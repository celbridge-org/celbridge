using Celbridge.Inspector.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.Inspector.Views;

public sealed partial class InspectorPanel : UserControl
{
    public LocalizedString Title => _stringLocalizer.GetString($"{nameof(InspectorPanel)}_{nameof(Title)}");

    private IStringLocalizer _stringLocalizer;

    public InspectorPanelViewModel ViewModel { get; }

    public InspectorPanel()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;
        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();

        ViewModel = serviceProvider.GetRequiredService<InspectorPanelViewModel>();

        var titleBar = new Grid()
            .Background(ThemeResource.Get<Brush>("PanelBackgroundBrush"))
            .BorderBrush(ThemeResource.Get<Brush>("PanelBorderBrush"))
            .BorderThickness(0, 0, 0, 1)
            .ColumnDefinitions("Auto, *")
            .Children(
                new TextBlock()
                    .Grid(column: 0)
                    .Text(Title)
                    .Margin(6, 0, 0, 0)
                    .VerticalAlignment(VerticalAlignment.Center)
            );

        var panelGrid = new Grid()
            .RowDefinitions("40, *")
            .Children(titleBar);
           
        //
        // Set the data context and page content
        // 

        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(panelGrid));
    }
}