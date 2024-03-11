using Celbridge.CommonViewModels.Pages;

namespace Celbridge.CommonViews.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPageViewModel ViewModel { get; private set; }

    public SettingsPage()
    {
        this.InitializeComponent();

        var serviceProvider = ServiceLocator.ServiceProvider;

        ViewModel = serviceProvider.GetRequiredService<SettingsPageViewModel>();

        Loaded += OnStartView_Loaded;
        Unloaded += OnStartView_Unloaded;
    }

    private void OnStartView_Loaded(object sender, RoutedEventArgs e)
    {}

    private void OnStartView_Unloaded(object sender, RoutedEventArgs e)
    {
        // Unregister all event handlers to avoid memory leaks

        Loaded -= OnStartView_Loaded;
        Unloaded -= OnStartView_Unloaded;
    }
}
