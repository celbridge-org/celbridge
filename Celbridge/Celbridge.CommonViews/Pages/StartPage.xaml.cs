using Celbridge.ViewModels.Pages;
using Microsoft.Extensions.Localization;

namespace Celbridge.CommonViews.Pages;

public sealed partial class StartPage : Page
{
    private IStringLocalizer _stringLocalizer;

    public string OpenWorkspace => _stringLocalizer.GetString($"{nameof(StartPage)}.{nameof(OpenWorkspace)}");

    public StartPageViewModel ViewModel { get; private set; }

    public StartPage()
    {
        this.InitializeComponent();

        var serviceProvider = ServiceLocator.ServiceProvider;
        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();

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
