using Celbridge.Project.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.Project.Views;

public sealed partial class ProjectPanel : UserControl
{
    public LocalizedString Title => _stringLocalizer.GetString($"{nameof(ProjectPanel)}_{nameof(Title)}");

    private IStringLocalizer _stringLocalizer;

    public ProjectPanelViewModel ViewModel { get; }

    public ProjectPanel()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;
        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();

        ViewModel = serviceProvider.GetRequiredService<ProjectPanelViewModel>();

        var titleBar = new Grid()
            .Background(ThemeResource.Get<Brush>("PanelBackgroundBrush"))
            .BorderBrush(ThemeResource.Get<Brush>("PanelBorderBrush"))
            .BorderThickness(0, 1, 0, 1)
            .ColumnDefinitions("Auto, *, Auto, 48")
            .Children(
                new TextBlock()
                    .Grid(column: 0)
                    .Text(Title)
                    .Margin(48, 0, 0, 0)
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