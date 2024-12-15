using Celbridge.Inspector.ViewModels;
using Windows.System;

namespace Celbridge.Inspector.Views;

public sealed partial class StringField : UserControl
{
    public StringFieldViewModel ViewModel { get; private set; }

    public StringField()
    {
        this.InitializeComponent();

        var serviceProvider = ServiceLocator.ServiceProvider;
        ViewModel = serviceProvider.GetRequiredService<StringFieldViewModel>();

        DataContext = ViewModel;

        Unloaded += (s, e) => ViewModel.OnViewUnloaded();
    }

    private void TextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            // Pressing enter moves focus to next property field

            var options = new FindNextElementOptions 
            { 
                SearchRoot = ((UIElement)sender).XamlRoot!.Content 
            };

            FocusManager.TryMoveFocus(FocusNavigationDirection.Next, options);

            e.Handled = true;
        }
    }
}
