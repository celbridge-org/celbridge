namespace Celbridge.CommonUI.Views;

public partial class MainMenuView : UserControl
{
    // Todo: Why is this gunk necessary?
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register("ViewModel", typeof(MainMenuViewModel), typeof(MainMenuView), new PropertyMetadata(null));

    public MainMenuViewModel ViewModel
    {
        get { return (MainMenuViewModel)GetValue(ViewModelProperty); }
        set { SetValue(ViewModelProperty, value); }
    }

    public MainMenuView()
    {
        this.InitializeComponent();

        var serviceProvider = BaseLibrary.Core.Services.ServiceProvider;

        ViewModel = serviceProvider.GetRequiredService<MainMenuViewModel>();
    }
}
