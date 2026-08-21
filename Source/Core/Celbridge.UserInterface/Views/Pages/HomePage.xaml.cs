using Celbridge.Projects;
using Celbridge.UserInterface.ViewModels.Pages;

namespace Celbridge.UserInterface.Views;

public sealed partial class HomePage : Page
{
    private IStringLocalizer _stringLocalizer;

    private string StartString => _stringLocalizer.GetString("HomePage_Start");
    private string NewProjectString => _stringLocalizer.GetString("HomePage_NewProject");
    private string NewProjectTooltipString => _stringLocalizer.GetString("HomePage_NewProjectTooltip");
    private string OpenProjectString => _stringLocalizer.GetString("HomePage_OpenProject");
    private string OpenProjectTooltipString => _stringLocalizer.GetString("HomePage_OpenProjectTooltip");
    private string CommunityString => _stringLocalizer.GetString("HomePage_Community");
    private string ForumString => _stringLocalizer.GetString("HomePage_Forum");
    private string ForumTooltipString => _stringLocalizer.GetString("HomePage_ForumTooltip");
    private string RecentString => _stringLocalizer.GetString("HomePage_Recent");

    public HomePageViewModel ViewModel { get; private set; }

    public HomePage()
    {
        this.InitializeComponent();

        ViewModel = ServiceLocator.AcquireService<HomePageViewModel>();

        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
    }

    private async void RecentProjectButton_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as HyperlinkButton;
        Guard.IsNotNull(button);

        var recentProject = button.DataContext as RecentProject;
        if (recentProject == null)
        {
            return;
        }

        var projectFilePath = Path.Combine(recentProject.ProjectFolderPath, $"{recentProject.ProjectName}{ProjectConstants.ProjectFileExtension}");
        await ViewModel.OpenProjectAsync(projectFilePath);
    }
}
