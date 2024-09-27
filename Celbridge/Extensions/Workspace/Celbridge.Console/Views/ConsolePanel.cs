using Celbridge.Console.Models;
using Celbridge.Console.ViewModels;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace Celbridge.Console.Views;

public partial class ConsolePanel : UserControl, IConsolePanel
{
    private const string PlayGlyph = "\ue768";
    private const string StrokeEraseGlyph = "\ued60";

    private readonly IStringLocalizer _stringLocalizer;

    // Todo: Add tooltip for execute button
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

        Unloaded += (sender, e) =>
        {
            ViewModel.ConsoleView_Unloaded();
        };

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
                            .VerticalAlignment(VerticalAlignment.Top)
                            .Margin(0,6,0,0)
                            .Glyph(() => item.Glyph),
                        new TextBlock()
                            .Text(() => item.LogText)
                            .TextWrapping(TextWrapping.Wrap)
                            .Margin(6, 0, 0, 0)
                            .Padding(0)
                    );
            })
            .ItemsSource(x => x.Binding(() => ViewModel.ConsoleLogItems).OneWay())
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

        _commandTextBox = new TextBox()
            .Grid(column: 0)
            .Background(ThemeResource.Get<Brush>("ApplicationBackgroundBrush"))
            .Text(x => x.Binding(() => ViewModel.CommandText)
                        .Mode(BindingMode.TwoWay)
                        .UpdateSourceTrigger(UpdateSourceTrigger.PropertyChanged))
            .VerticalAlignment(VerticalAlignment.Bottom)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .IsSpellCheckEnabled(false)
            .TextWrapping(TextWrapping.Wrap)
            .AcceptsReturn(true);

        _commandTextBox.KeyDown += CommandTextBox_KeyDown;

#if WINDOWS
        // On Windows, AcceptsReturn causes the TextBox to swallow the KeyDown event for Enter, so we need to
        // use PreviewKeyDown to intercept it.
        _commandTextBox.PreviewKeyDown += CommandTextBox_PreviewKeyDown;
#endif

        var submitButton = new Button()
            .Grid(column: 1)
            .VerticalAlignment(VerticalAlignment.Bottom)
            .Command(ViewModel.SubmitCommand)
            .Content
            (
                new FontIcon()
                    .FontFamily(fontFamily)
                    .Glyph(PlayGlyph)
            );

        // Todo: Localize this tooltip
        ToolTipService.SetToolTip(submitButton, "Shift + Enter to run command");

        var commandLine = new Grid()
            .Grid(row: 1)
            .ColumnDefinitions("*, auto")
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .Children(_commandTextBox, submitButton);

        var consoleGrid = new Grid()
            .RowDefinitions("*, auto")
            .VerticalAlignment(VerticalAlignment.Stretch)
            .Children(_scrollViewer, commandLine);

        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(consoleGrid));
    }

#if WINDOWS
    private void CommandTextBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            CoreVirtualKeyStates shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
            bool shift = (shiftState & CoreVirtualKeyStates.Down) != 0;

            if (shift)
            {
                ViewModel.SubmitCommand.Execute(this);

                // Scroll to the end of the output list
                _scrollViewer.UpdateLayout();
                _scrollViewer.ScrollToVerticalOffset(_scrollViewer.ScrollableHeight);

                e.Handled = true;
            }
        }
    }
#endif

    public void CommandTextBox_KeyDown(object? sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            // This does not get called on Windows, because of the issue with AcceptsReturn described above.
            CoreVirtualKeyStates shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
            bool shift = (shiftState & CoreVirtualKeyStates.Down) != 0;

            if (shift)
            {
                ViewModel.SubmitCommand.Execute(this);

                // Scroll to the end of the output list
                _scrollViewer.UpdateLayout();
                _scrollViewer.ScrollToVerticalOffset(_scrollViewer.ScrollableHeight);

            }
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
