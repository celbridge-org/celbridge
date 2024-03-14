using Celbridge.Console.ViewModels;

namespace Celbridge.Console.Views;

public sealed partial class ConsolePanel : UserControl
{
    private readonly FontFamily IconFontFamily = new FontFamily("Segoe MDL2 Assets");
    private const string StrokeEraseGlyph = "\ued60";

    public ConsolePanelViewModel ViewModel { get; }

    public ConsolePanel()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;
        ViewModel = serviceProvider.GetRequiredService<ConsolePanelViewModel>();

        var clearButton = new Button()
            .HorizontalAlignment(HorizontalAlignment.Left)
            .VerticalAlignment(VerticalAlignment.Top)
            .Command(ViewModel.ClearCommand)
            .Content(new FontIcon
            {
                FontFamily = IconFontFamily,
                Glyph = StrokeEraseGlyph,
            });

        Content = clearButton;
    }
}
