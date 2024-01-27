namespace Celbridge.Legacy.Views;

public sealed partial class StatusBar : UserControl
{
    public StatusBarViewModel ViewModel { get; set; }

    public StatusBar()
    {
        this.InitializeComponent();
        ViewModel = LegacyServiceProvider.Services!.GetRequiredService<StatusBarViewModel>();
    }
}
