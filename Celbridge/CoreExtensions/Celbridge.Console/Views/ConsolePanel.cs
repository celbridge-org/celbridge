using Celbridge.Console.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.Console.Views;

public sealed partial class ConsolePanel : UserControl
{
    private readonly FontFamily IconFontFamily = new FontFamily("Segoe MDL2 Assets");
    private const string StrokeEraseGlyph = "\ued60";

    public string Title => _stringLocalizer.GetString($"{nameof(ConsolePanel)}.{nameof(Title)}");
    public string ClearButtonTooltip => _stringLocalizer.GetString($"{nameof(ConsolePanel)}.{nameof(ClearButtonTooltip)}");

    private IStringLocalizer _stringLocalizer;

    public ConsolePanelViewModel ViewModel { get; }

    public ConsolePanel()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;
        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();

        ViewModel = serviceProvider.GetRequiredService<ConsolePanelViewModel>();

        var clearButton = new Button()
            .Grid(column: 2)
            .Command(ViewModel.ClearCommand)
            .Content(new FontIcon
            {
                FontFamily = IconFontFamily,
                Glyph = StrokeEraseGlyph,
            });

        ToolTipService.SetToolTip(clearButton, ClearButtonTooltip);
        ToolTipService.SetPlacement(clearButton, PlacementMode.Top);

        var titleBar = new Grid()
            .Background(ThemeResource.Get<Brush>("PanelBackgroundBrush"))
            .BorderBrush(ThemeResource.Get<Brush>("PanelBorderBrush"))
            .BorderThickness(0, 1, 0, 1)
            .ColumnDefinitions("Auto, *, Auto, 48")
            .Children(
                new TextBlock()
                    .Grid(column: 0)
                    .Text(Title)
                    .VerticalAlignment(VerticalAlignment.Center),
                clearButton);

        var panelGrid = new Grid()
            .RowDefinitions("40, *, 32")
            .Children(titleBar);
           
        //
        // Set the data context and page content
        // 
        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(panelGrid));
    }
}
