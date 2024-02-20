using CommunityToolkit.Mvvm.Input;

namespace Celbridge.CommonUI.Views;

public sealed partial class StartPage : Page
{
    public StartPageViewModel ViewModel { get; private set; }

    public StartPage()
    {
        this.InitializeComponent();

        var serviceProvider = Services.ServiceProvider;

        ViewModel = serviceProvider.GetRequiredService<StartPageViewModel>();

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
