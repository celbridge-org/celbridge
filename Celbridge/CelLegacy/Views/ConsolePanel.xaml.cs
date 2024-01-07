using Microsoft.UI;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace CelLegacy.Views;

public sealed partial class ConsolePanel : UserControl
{
    public ConsoleViewModel ViewModel { get; }

    public ConsolePanel()
    {
        this.InitializeComponent();
        ViewModel = LegacyServiceProvider.Services!.GetRequiredService<ConsoleViewModel>();

        ViewModel.OnWriteMessage += (message, logType) =>
        {
            // Avoid double newlines
            //string trimmed = message.TrimEnd('\r', '\n');

            var run = new Run();
            run.Text = message.Trim();
            switch (logType)
            {
                case Services.ConsoleLogType.Ok:
                    run.Foreground = new SolidColorBrush(Colors.LightGreen);
                    break;
                case Services.ConsoleLogType.Error:
                    run.Foreground = new SolidColorBrush(Colors.Red);
                    break;
                case Services.ConsoleLogType.Warn:
                    run.Foreground = new SolidColorBrush(Colors.Yellow);
                    break;
                case Services.ConsoleLogType.Info:
                default:
                    break;
            }

            var paragraph = new Paragraph();
            paragraph.Inlines.Add(run);
            ConsoleLogText.Blocks.Add(paragraph);

            // Update the textblock so the scroll height is up to date
            ConsoleLogText.UpdateLayout();

            // Scroll to the bottom
            ConsoleScrollViewer.ChangeView(null, ConsoleScrollViewer.ScrollableHeight, null);
        };

        ViewModel.OnClearMessages += () =>
        {
            ConsoleLogText.Blocks.Clear();
        };

        ViewModel.OnCommandEntered += () =>
        {
            ConsoleScrollViewer.ScrollToVerticalOffset(ConsoleScrollViewer.ScrollableHeight);
        };

        ViewModel.OnHistoryCycled += () =>
        {
            CommandTextBox.SelectionStart = CommandTextBox.Text.Length;
        };
    }

    public void CommandTextBox_KeyDown(object? sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Up || e.Key == VirtualKey.Down)
        {
            if (e.Key == VirtualKey.Up)
            {
                ViewModel.CycleHistoryCommand.Execute(false);
            }
            else if (e.Key == VirtualKey.Down)
            {
                ViewModel.CycleHistoryCommand.Execute(true);
            }
            e.Handled = true; // Mark the event as handled to prevent further processing
        }
    }

    public void CommandTextBox_KeyUp(object? sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ViewModel.SubmitCommand.Execute(null);
        }
    }
}
