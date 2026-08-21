using Celbridge.Projects;
using Celbridge.UserInterface.ViewModels;

namespace Celbridge.UserInterface.Views;

public sealed partial class HomeView : UserControl
{
    private IStringLocalizer _stringLocalizer;

    private string StartString => _stringLocalizer.GetString("Home_Start");
    private string NewProjectString => _stringLocalizer.GetString("Home_NewProject");
    private string NewProjectTooltipString => _stringLocalizer.GetString("Home_NewProjectTooltip");
    private string OpenProjectString => _stringLocalizer.GetString("Home_OpenProject");
    private string OpenProjectTooltipString => _stringLocalizer.GetString("Home_OpenProjectTooltip");
    private string CommunityString => _stringLocalizer.GetString("Home_Community");
    private string LearnString => _stringLocalizer.GetString("Home_Learn");
    private string LearnTooltipString => _stringLocalizer.GetString("Home_LearnTooltip");
    private string ForumString => _stringLocalizer.GetString("Home_Forum");
    private string ForumTooltipString => _stringLocalizer.GetString("Home_ForumTooltip");
    private string RecentString => _stringLocalizer.GetString("Home_Recent");

    public HomeViewModel ViewModel { get; private set; }

    public HomeView()
    {
        this.InitializeComponent();

        ViewModel = ServiceLocator.AcquireService<HomeViewModel>();

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
