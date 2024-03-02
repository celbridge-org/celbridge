using Celbridge.CommonViewModels.Pages;

namespace Celbridge.CommonViews.Pages;

public sealed partial class NewProjectPage : Page
{
    public NewProjectPageViewModel ViewModel { get; private set; }

    public NewProjectPage()
    {
        this.InitializeComponent();

        var serviceProvider = Services.ServiceProvider;

        ViewModel = serviceProvider.GetRequiredService<NewProjectPageViewModel>();

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
