namespace Celbridge.UserInterface.Views;

public sealed partial class HomePage : Page
{
    public HomePageViewModel ViewModel { get; private set; }

    public HomePage()
    {
        this.InitializeComponent();

        ViewModel = ServiceLocator.ServiceProvider.GetRequiredService<HomePageViewModel>();
    }
}
