namespace Celbridge.UserInterface.Views;

public sealed partial class HomePage : Page
{
    private IStringLocalizer _stringLocalizer;

    public LocalizedString TitleString => _stringLocalizer.GetString("HomePage_Title");
    public LocalizedString SubtitleString => _stringLocalizer.GetString("HomePage_Subtitle");
    public LocalizedString StartString => _stringLocalizer.GetString("HomePage_Start");
    public LocalizedString NewProjectString => _stringLocalizer.GetString("HomePage_NewProject");
    public LocalizedString OpenProjectString => _stringLocalizer.GetString("HomePage_OpenProject");
    public LocalizedString RecentlyOpenedString => _stringLocalizer.GetString("HomePage_RecentlyOpened");
    public LocalizedString NoRecentProjectsString => _stringLocalizer.GetString("HomePage_NoRecentProjects");

    public HomePageViewModel ViewModel { get; private set; }

    public HomePage()
    {
        this.InitializeComponent();

        var serviceProvider = ServiceLocator.ServiceProvider;
        ViewModel = serviceProvider.GetRequiredService<HomePageViewModel>();

        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();
    }
}
