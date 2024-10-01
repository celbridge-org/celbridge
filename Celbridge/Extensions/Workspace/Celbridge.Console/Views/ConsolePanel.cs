using Celbridge.Console.Models;
using Celbridge.Console.ViewModels;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;

namespace Celbridge.Console.Views;

public partial class ConsolePanel : UserControl, IConsolePanel
{
    private readonly IStringLocalizer _stringLocalizer;

    private LocalizedString ExecuteCommandTooltipString => _stringLocalizer.GetString("ConsolePanel_ExecuteCommandTooltip");
    private LocalizedString ClearButtonTooltipString => _stringLocalizer.GetString("ConsolePanel_ClearButtonTooltip");

    private readonly ScrollViewer _scrollViewer;
    private readonly TextBox _commandTextBox;

    public ConsolePanelViewModel ViewModel { get; }

    public ConsolePanel(
        IServiceProvider serviceProvider,
        IStringLocalizer stringLocalizer)
    {
        _stringLocalizer = stringLocalizer;

        ViewModel = serviceProvider.GetRequiredService<ConsolePanelViewModel>();

        ViewModel.LogEntryAdded += () =>
        {
            Guard.IsNotNull(_scrollViewer);

            // Scroll to the end of the output list
            _scrollViewer.UpdateLayout();
            _scrollViewer.ScrollToVerticalOffset(_scrollViewer.ScrollableHeight);
        };

        Unloaded += (sender, e) =>
        {
            ViewModel.ConsoleView_Unloaded();
        };

        var symbolFontFamily = ThemeResource.Get<FontFamily>("SymbolThemeFontFamily");

        var listView = new ListView()
            .ItemTemplate<ConsoleLogItem>(item =>
            {
                var fontIcon = new FontIcon()
                    .Grid(column: 0)
                    .FontFamily(symbolFontFamily)
                    .FontSize(14)
                    .Foreground(() => item.Color)
                    .VerticalAlignment(VerticalAlignment.Top)
                    .Margin(0, 4, 0, 0)
                    .Glyph(() => item.Glyph);

                var textBlock = new TextBlock()
                    .Grid(column: 1)
                    .Text(() => item.LogText)
                    .Margin(6, 0, 0, 0)
                    .Padding(0)
                    .MinHeight(16)
                    .TextWrapping(TextWrapping.Wrap)
                    .IsHitTestVisible(true)
                    .IsTextSelectionEnabled(true);

                return new Grid()
                    .ColumnDefinitions("16, *")
                    .Children(fontIcon, textBlock);
            })
            .ItemsSource(x => x.Binding(() => ViewModel.ConsoleLogItems).OneWay())
            .ItemContainerStyle(new Style(typeof(ListViewItem))
            {
                Setters =
                {
                    new Setter(Control.PaddingProperty, new Thickness(0)), // Remove padding inside each item
                    new Setter(FrameworkElement.MarginProperty, new Thickness(6, 0, 6, 0)), // Minimal vertical margin between items
                    new Setter(FrameworkElement.MinHeightProperty, 24)
                }
            });

        listView.Transitions.Clear();

        _scrollViewer = new ScrollViewer()
            .Grid(row: 0)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .Background(ThemeResource.Get<Brush>("PanelBackgroundBBrush"))
            .Content(listView);

        _commandTextBox = new TextBox()
            .Grid(column: 0)
            .Margin(0, 0, 2, 0)
            .Background(ThemeResource.Get<Brush>("ApplicationBackgroundBrush"))
            .Text(x => x.Binding(() => ViewModel.CommandText)
                        .Mode(BindingMode.TwoWay)
                        .UpdateSourceTrigger(UpdateSourceTrigger.PropertyChanged))
            .VerticalAlignment(VerticalAlignment.Bottom)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .IsSpellCheckEnabled(false);

        _commandTextBox.KeyDown += CommandTextBox_KeyDown;

        var executeButton = new Button()
            .Grid(column: 1)
            .Margin(0, 0, 2, 0)
            .Command(ViewModel.ExecuteCommand)
            .Content
            (
                new SymbolIcon()
                .Symbol(Symbol.Play)
            );

        var clearButton = new Button()
            .Grid(column: 2)
            .Margin(0)
            .Command(ViewModel.ClearLogCommand)
            .Content
            (
                new SymbolIcon()
                .Symbol(Symbol.Clear)
            );

        ToolTipService.SetToolTip(executeButton, ExecuteCommandTooltipString);
        ToolTipService.SetToolTip(clearButton, ClearButtonTooltipString);

        var commandLine = new Grid()
            .Grid(row: 1)
            .ColumnDefinitions("*, auto, auto")
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .Children(_commandTextBox, executeButton, clearButton);

        var consoleGrid = new Grid()
            .RowDefinitions("*, auto")
            .VerticalAlignment(VerticalAlignment.Stretch)
            .Children(_scrollViewer, commandLine);

        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(consoleGrid));
    }

    public void CommandTextBox_KeyDown(object? sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ViewModel.ExecuteCommand.Execute(this);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Up)
        {
            ViewModel.SelectPreviousCommand.Execute(this);
            e.Handled = true;
            _commandTextBox.SelectionStart = _commandTextBox.Text.Length;
        }
        else if (e.Key == VirtualKey.Down)
        {
            ViewModel.SelectNextCommand.Execute(this);
            e.Handled = true;
            _commandTextBox.SelectionStart = _commandTextBox.Text.Length;
        }
    }
}
