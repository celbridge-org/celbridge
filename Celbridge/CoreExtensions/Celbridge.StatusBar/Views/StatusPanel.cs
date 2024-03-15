using Celbridge.StatusBar.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.StatusBar.Views;

public class StatusPanel : UserControl
{
    private readonly FontFamily IconFontFamily = new FontFamily("Segoe MDL2 Assets");
    private const string SaveGlyph = "\ue74e";

    private IStringLocalizer _stringLocalizer;

    public StatusPanelViewModel ViewModel { get; }

    public StatusPanel()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;
        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();

        ViewModel = serviceProvider.GetRequiredService<StatusPanelViewModel>();

        var panelGrid = new Grid()
            .ColumnDefinitions("*, Auto, Auto, 48")
            .VerticalAlignment(VerticalAlignment.Center)
            .Children(
                new TextBlock()
                    .Grid(column: 1)
                    .Margin(6, 3)
                    .Text("<Placeholder text>"),

                new Button()
                    .Grid(column: 2)
                    .Content(
                        new FontIcon
                        {
                            FontFamily = IconFontFamily,
                            Glyph = SaveGlyph,
                        }
                )
            );

        //
        // Set the data context and page content
        // 

        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(panelGrid));
    }
}
