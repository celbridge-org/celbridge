namespace Celbridge.CommonUI.Views;

public partial class MainMenuView : UserControl
{
    public MainMenuViewModel ViewModel { get; set; }

    public MainMenuView()
    {
        this.InitializeComponent();

        var serviceProvider = Services.ServiceProvider;

        ViewModel = serviceProvider.GetRequiredService<MainMenuViewModel>();
    }
}
