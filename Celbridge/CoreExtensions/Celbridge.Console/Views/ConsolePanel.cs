using Celbridge.Console.ViewModels;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml.Controls;

namespace Celbridge.Console.Views;

public sealed partial class ConsolePanel : UserControl
{
    private const string StrokeEraseGlyph = "\ued60";

    public LocalizedString Title => _stringLocalizer.GetString($"{nameof(ConsolePanel)}_{nameof(Title)}");
    public LocalizedString ClearButtonTooltip => _stringLocalizer.GetString($"{nameof(ConsolePanel)}_{nameof(ClearButtonTooltip)}");

    private IStringLocalizer _stringLocalizer;

    private TabView _tabView;

    public ConsolePanelViewModel ViewModel { get; }

    public ConsolePanel()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;
        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();

        ViewModel = serviceProvider.GetRequiredService<ConsolePanelViewModel>();

        var fontFamily = ThemeResource.Get<FontFamily>("SymbolThemeFontFamily");

        var clearButton = new Button()
            .Grid(column: 2)
            .Command(ViewModel.ClearCommand)
            .Content(new FontIcon()
                .FontFamily(fontFamily)
                .Glyph(StrokeEraseGlyph)
            );

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
                    .Margin(6, 0, 0, 0)
                    .VerticalAlignment(VerticalAlignment.Center),
                clearButton);

        //var consolePanelGrid = new Grid()
        //    .RowDefinitions("40, *")
        //    .Children(titleBar, consoleTabItem);

        _tabView = new TabView()
            .TabItemsSource(x => x.Bind(() => ViewModel.ConsoleTabs).OneWay())
            .TabStripHeader("Console")
            .TabItemTemplate<ConsoleTabItemViewModel>(item => 
            {
                //var headerText = new TextBlock()
                //     .Text(x => x.Bind(() => item.HeaderText));

                //var header = new StackPanel()
                //    .Orientation(Orientation.Horizontal)
                //    .Children(headerText);

                var tabItem = new ConsoleTabItem()
                    //.HeaderTemplate(() => new TextBlock().Text("Console"))
                    .VerticalAlignment(VerticalAlignment.Bottom);

                return tabItem;
            })
            .TabWidthMode(TabViewWidthMode.SizeToContent)
            .VerticalAlignment(VerticalAlignment.Stretch)
            .Background(ThemeResource.Get<Brush>("PanelBackgroundABrush"))
            .IsAddTabButtonVisible(true)
            .AddTabButtonCommand(ViewModel.AddConsoleTabCommand);

        //_tabView.TabCloseRequested += ConsolePanel_TabCloseRequested;

        //
        // Set the data context and page content
        // 

        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(_tabView));
    }

    private void ConsolePanel_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
    }
}