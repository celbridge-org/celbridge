using Celbridge.Console.ViewModels;
using Microsoft.Extensions.Localization;
using Windows.System;

namespace Celbridge.Console.Views;

public sealed partial class ConsolePanel : UserControl
{
    private const string StrokeEraseGlyph = "\ued60";

    public LocalizedString Title => _stringLocalizer.GetString($"{nameof(ConsolePanel)}_{nameof(Title)}");
    public LocalizedString ClearButtonTooltip => _stringLocalizer.GetString($"{nameof(ConsolePanel)}_{nameof(ClearButtonTooltip)}");

    private IStringLocalizer _stringLocalizer;

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

        var scrollViewer = new ScrollViewer()
            .Grid(row: 1)
            .Background(ThemeResource.Get<Brush>("PanelBackgroundBBrush"))
            .Content(new ListView()
                .ItemTemplate(() =>
                {
                    var textBlock = new TextBlock()
                        .Text(x => x.Bind(() => x))
                        .Margin(0)
                        .Padding(0);
                    return textBlock;
                })
                .ItemsSource(x => x.Bind(() => ViewModel.OutputItems).OneWay())
                .ItemContainerStyle(new Style(typeof(ListViewItem))
                {
                    Setters =
                    {
                        new Setter(Control.PaddingProperty, new Thickness(0)), // Remove padding inside each item
                        new Setter(FrameworkElement.MarginProperty, new Thickness(6, 0, 6, 0)), // Minimal vertical margin between items
                        new Setter(FrameworkElement.MinHeightProperty, 24),
                    }
                })
            );

        var inputTextBox = new TextBox()
            .Grid(row: 2)
            .Text("<Placeholder text>")
            .Background(ThemeResource.Get<Brush>("ApplicationBackgroundBrush"))
            .Text(x => x.Bind(() => ViewModel.InputText)
                        .Mode(BindingMode.TwoWay)
                        .UpdateSourceTrigger(UpdateSourceTrigger.PropertyChanged))
            .VerticalAlignment(VerticalAlignment.Bottom)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .IsSpellCheckEnabled(false);

        inputTextBox.KeyDown += CommandTextBox_KeyDown;
        inputTextBox.KeyUp += CommandTextBox_KeyUp;

        var panelGrid = new Grid()
            .RowDefinitions("40, *, 32")
            .Children(titleBar, scrollViewer, inputTextBox);
           
        //
        // Set the data context and page content
        // 

        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(panelGrid));
    }

    public void CommandTextBox_KeyDown(object? sender, KeyRoutedEventArgs e)
    {
        //if (e.Key == VirtualKey.Up || e.Key == VirtualKey.Down)
        //{
        //    if (e.Key == VirtualKey.Up)
        //    {
        //        ViewModel.CycleHistoryCommand.Execute(false);
        //    }
        //    else if (e.Key == VirtualKey.Down)
        //    {
        //        ViewModel.CycleHistoryCommand.Execute(true);
        //    }
        //    e.Handled = true; // Mark the event as handled to prevent further processing
        //}
    }

    public void CommandTextBox_KeyUp(object? sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ViewModel.SubmitCommand.Execute(null);
        }
    }
}