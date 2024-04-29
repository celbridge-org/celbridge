using Celbridge.Console.ViewModels;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;

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

        ViewModel = CreateViewModel();

        _tabView = CreateTabView();

        // Todo: Provide an extension point to add custom toolbar buttons here
        var clearButton = CreateButton(StrokeEraseGlyph, ClearButtonTooltip, ViewModel.ClearCommand);

        // The third column here prevents the clear button from overlapping the panel collapse button
        var panelContent = new Grid()
            .ColumnDefinitions("*, Auto, 48")
            .Children
            (
                _tabView,
                clearButton
            );

        //
        // Set the data context page content
        // 

        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(panelContent));
    }

    private ConsolePanelViewModel CreateViewModel()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;
        var viewModel = serviceProvider.GetRequiredService<ConsolePanelViewModel>();

        viewModel.OnAddConsoleTab += () =>
        {
            CreateConsoleTabViewItem(_tabView);
        };

        viewModel.OnClearConsole += () =>
        {
            var tabViewItem = _tabView.SelectedItem as TabViewItem;
            Guard.IsNotNull(tabViewItem);

            var consoleView = tabViewItem.Content as ConsoleView;
            Guard.IsNotNull(consoleView);

            consoleView.ViewModel.ClearCommand.Execute(this);
        };

        return viewModel;
    }

    private TabView CreateTabView()
    {
        var tabView = new TabView()
            .Grid(columnSpan: 3)
            .TabWidthMode(TabViewWidthMode.SizeToContent)
            .VerticalAlignment(VerticalAlignment.Stretch)
            .Background(ThemeResource.Get<Brush>("PanelBackgroundABrush"))
            .IsAddTabButtonVisible(true)
            .AddTabButtonCommand(ViewModel.AddConsoleTabCommand)
#if WINDOWS
            .CanReorderTabs(true)
            .CanDragTabs(true)
#endif
            .TabStripFooter
            (
                new Grid()
                    .Width(96)
                    .Height(40)
                    .HorizontalAlignment(HorizontalAlignment.Stretch)
            );

        CreateConsoleTabViewItem(tabView);

        return tabView;
    }

    private UIElement CreateButton(string iconGlyph, LocalizedString tooltipText, ICommand command)
    {
        var fontFamily = ThemeResource.Get<FontFamily>("SymbolThemeFontFamily");

        var button = new Button()
            .Grid(column: 1)
            .HorizontalAlignment(HorizontalAlignment.Right)
            .VerticalAlignment(VerticalAlignment.Top)
            .Command(command)
            .Content
            (
                new FontIcon()
                    .FontFamily(fontFamily)
                    .Glyph(iconGlyph)
            );

        ToolTipService.SetToolTip(button, tooltipText);
        ToolTipService.SetPlacement(button, PlacementMode.Top);

        return button!;
    }

    private void CreateConsoleTabViewItem(TabView tabView)
    {
        var tabViewItem = new TabViewItem()
            .Header("Console")
            .Content(new ConsoleView());

        tabViewItem.CloseRequested += (sender, args) =>
        {
            var consoleView = sender.Content as ConsoleView;
            Guard.IsNotNull(consoleView);

            // Give the view model an opportunity to handle the close event
            consoleView.ViewModel.CloseCommand.Execute(this);

            tabView.TabItems.Remove(args.Tab);
        };
        
        tabView.TabItems.Add(tabViewItem);
        tabView.SelectedItem = tabViewItem;
    }
}