using Celbridge.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Celbridge.Views
{
    public sealed partial class ConsolePanel : UserControl
    {
        public ConsoleViewModel ViewModel { get; }

        public ConsolePanel()
        {
            this.InitializeComponent();
            ViewModel = (Application.Current as App).Host.Services.GetRequiredService<ConsoleViewModel>();

            ViewModel.OnWriteMessage += () =>
            {
                ConsoleScrollViewer.ScrollToVerticalOffset(ConsoleScrollViewer.ScrollableHeight);
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

        public void CommandTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
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

        public void CommandTextBox_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                ViewModel.SubmitCommand.Execute(null);
            }
        }
    }
}
