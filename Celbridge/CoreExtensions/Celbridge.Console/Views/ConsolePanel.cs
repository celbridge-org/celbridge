using Celbridge.Console.ViewModels;
using Microsoft.Extensions.Localization;
using Windows.System;

namespace Celbridge.Console.Views;

public sealed partial class ConsolePanel : UserControl
{
    private const string StrokeEraseGlyph = "\ued60";
    private const string ChevronRightGlyph = "\ue76C";
    private const string InfoGlyph = "\ue946";
    private const string WarningGlyph = "\ue7BA";
    private const string ErrorGlyph = "\ue783";

    public LocalizedString Title => _stringLocalizer.GetString($"{nameof(ConsolePanel)}_{nameof(Title)}");
    public LocalizedString ClearButtonTooltip => _stringLocalizer.GetString($"{nameof(ConsolePanel)}_{nameof(ClearButtonTooltip)}");

    private IStringLocalizer _stringLocalizer;

    private ScrollViewer _scrollViewer;
    private TextBox _commandTextBox;

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

        _scrollViewer = new ScrollViewer()
            .Grid(row: 1)
            .Background(ThemeResource.Get<Brush>("PanelBackgroundBBrush"))
            .Content(new ListView()
                .ItemTemplate<ConsoleLogItem>(item =>
                {
                    return new StackPanel()
                        .Orientation(Orientation.Horizontal)
                        .Children(
                            new FontIcon()
                                .FontFamily(fontFamily)
                                .FontSize(12)
                                .Foreground(() => item.Color)
                                .VerticalAlignment(VerticalAlignment.Center)
                                .Margin(0)
                                .Glyph(() => item.Glyph),
                            new TextBlock()
                                .Text(() => item.LogText)
                                .Margin(6, 0, 0, 0)
                                .Padding(0)
                            );
                })
                .ItemsSource(x => x.Bind(() => ViewModel.ConsoleLogItems).OneWay())
                .ItemContainerStyle(new Style(typeof(ListViewItem))
                {
                    Setters =
                    {
                        new Setter(Control.PaddingProperty, new Thickness(0)), // Remove padding inside each item
                        new Setter(FrameworkElement.MarginProperty, new Thickness(6, 0, 6, 0)), // Minimal vertical margin between items
                        new Setter(FrameworkElement.MinHeightProperty, 24),
                        new Setter(FrameworkElement.HeightProperty, 24),
                    }
                })
            );

        _commandTextBox = new TextBox()
            .Grid(row: 2)
            .Background(ThemeResource.Get<Brush>("ApplicationBackgroundBrush"))
            .Text(x => x.Bind(() => ViewModel.CommandText)
                        .Mode(BindingMode.TwoWay)
                        .UpdateSourceTrigger(UpdateSourceTrigger.PropertyChanged))
            .VerticalAlignment(VerticalAlignment.Bottom)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .IsSpellCheckEnabled(false);

        _commandTextBox.KeyDown += CommandTextBox_KeyDown;
        _commandTextBox.KeyUp += CommandTextBox_KeyUp;

        var panelGrid = new Grid()
            .RowDefinitions("40, *, 32")
            .Children(titleBar, _scrollViewer, _commandTextBox);

        //
        // Set the data context and page content
        // 

        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(panelGrid));
    }

    public void CommandTextBox_KeyDown(object? sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Up)
        {
            ViewModel.SelectPreviousCommand.Execute(this);
            e.Handled = true; // Mark the event as handled to prevent further processing
            _commandTextBox.SelectionStart = _commandTextBox.Text.Length;
        }
        else if (e.Key == VirtualKey.Down)
        {
            ViewModel.SelectNextCommand.Execute(this);
            e.Handled = true;
            _commandTextBox.SelectionStart = _commandTextBox.Text.Length;
        }
    }

    public void CommandTextBox_KeyUp(object? sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ViewModel.SubmitCommand.Execute(this);
            e.Handled = true;

            // Scroll to the end of the output list
            _scrollViewer.UpdateLayout();
            _scrollViewer.ScrollToVerticalOffset(_scrollViewer.ScrollableHeight);
        }
    }
}