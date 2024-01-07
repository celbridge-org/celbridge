namespace CelLegacy.Views;

public partial class MainMenu : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register("ViewModel", typeof(MainMenuViewModel), typeof(MainMenu), new PropertyMetadata(null));

    public MainMenuViewModel ViewModel
    {
        get { return (MainMenuViewModel)GetValue(ViewModelProperty); }
        set { SetValue(ViewModelProperty, value); }
    }

    public MainMenu()
    {
        this.InitializeComponent();
        ViewModel = LegacyServiceProvider.Services!.GetRequiredService<MainMenuViewModel>();
    }
}
