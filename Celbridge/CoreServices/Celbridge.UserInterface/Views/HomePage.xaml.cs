namespace Celbridge.UserInterface.Views;

public sealed partial class HomePage : Page
{
    private IStringLocalizer _stringLocalizer;

    public LocalizedString NewProjectString => _stringLocalizer.GetString("MainPage_NewProject");
    public LocalizedString OpenProjectString => _stringLocalizer.GetString("MainPage_OpenProject");

    public HomePageViewModel ViewModel { get; private set; }

    public HomePage()
    {
        this.InitializeComponent();

        var serviceProvider = ServiceLocator.ServiceProvider;
        ViewModel = serviceProvider.GetRequiredService<HomePageViewModel>();

        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();
    }
}
