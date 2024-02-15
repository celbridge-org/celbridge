namespace Celbridge.CommonUI.Views;

public sealed partial class StartView : Page
{
    public StartViewModel ViewModel { get; private set; }

    public StartView()
    {
        this.InitializeComponent();

        var serviceProvider = BaseLibrary.Core.Services.ServiceProvider;

        ViewModel = serviceProvider.GetRequiredService<StartViewModel>();
    }
}
