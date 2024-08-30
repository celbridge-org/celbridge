using Celbridge.StatusBar.ViewModels;

namespace Celbridge.StatusBar.Views;

public class StatusPanel : UserControl
{
    private const string SaveGlyph = "\ue74e";

    public StatusPanelViewModel ViewModel { get; }

    public StatusPanel()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;

        ViewModel = serviceProvider.GetRequiredService<StatusPanelViewModel>();

        Loaded += (s, e) => ViewModel.OnLoaded();
        Unloaded += (s, e) => ViewModel.OnUnloaded();

        var fontFamily = ThemeResource.Get<FontFamily>("SymbolThemeFontFamily");

        var panelGrid = new Grid()
            .ColumnDefinitions("*, Auto, Auto, 48")
            .VerticalAlignment(VerticalAlignment.Center)
            .Children
            (
                new TextBlock()
                    .Grid(column: 1)
                    .Margin(6, 3)
                    .Text(x => x.Bind(() => ViewModel.StatusText)),
                new FontIcon()
                    .Grid(column: 2)
                    .HorizontalAlignment(HorizontalAlignment.Right)
                    .Opacity(x => x.Bind(() => ViewModel.SaveIconOpacity))
                    .FontFamily(fontFamily)
                    .Glyph(SaveGlyph)
            );

        //
        // Set the data context and page content
        // 

        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(panelGrid));
    }
}
