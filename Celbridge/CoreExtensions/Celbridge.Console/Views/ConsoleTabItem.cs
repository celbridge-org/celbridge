using Celbridge.Console.ViewModels;
using Microsoft.Extensions.Localization;
using Windows.System;

namespace Celbridge.Console.Views;

public class ConsoleTabItem : TabViewItem
{
    private ScrollViewer _scrollViewer;
    private TextBox _commandTextBox;

    private IStringLocalizer _stringLocalizer;

    public ConsoleTabItemViewModel ViewModel { get; }

    public ConsoleTabItem()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;
        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();

        ViewModel = serviceProvider.GetRequiredService<ConsoleTabItemViewModel>();

        var fontFamily = ThemeResource.Get<FontFamily>("SymbolThemeFontFamily");

        _scrollViewer = new ScrollViewer()
            .Grid(row: 0)
            .Background(ThemeResource.Get<Brush>("PanelBackgroundBBrush"))
            .Content(new ListView()
            .ItemTemplate<ConsoleLogItem>(item =>
            {
                return new StackPanel()
                    .Orientation(Orientation.Horizontal)
                    .Children
                    (
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
            .Grid(row: 1)
            .Background(ThemeResource.Get<Brush>("ApplicationBackgroundBrush"))
            .Text(x => x.Bind(() => ViewModel.CommandText)
                        .Mode(BindingMode.TwoWay)
                        .UpdateSourceTrigger(UpdateSourceTrigger.PropertyChanged))
            .VerticalAlignment(VerticalAlignment.Bottom)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .IsSpellCheckEnabled(false);

        _commandTextBox.KeyDown += CommandTextBox_KeyDown;
        _commandTextBox.KeyUp += CommandTextBox_KeyUp;

        var consoleGrid = new Grid()
            .RowDefinitions("*, 32")
            .VerticalAlignment(VerticalAlignment.Stretch)
            .Children(_scrollViewer, _commandTextBox);

        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(consoleGrid));
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
