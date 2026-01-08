using Celbridge.Projects;
using Celbridge.UserInterface.Models;
using Celbridge.UserInterface.ViewModels.Pages;

namespace Celbridge.UserInterface.Views;

public sealed partial class HomePage : Page
{
    private IStringLocalizer _stringLocalizer;

    private LocalizedString TitleString => _stringLocalizer.GetString("HomePage_Title");
    private LocalizedString SubtitleString => _stringLocalizer.GetString("HomePage_Subtitle");
    private LocalizedString StartString => _stringLocalizer.GetString("HomePage_Start");
    private LocalizedString NewProjectString => _stringLocalizer.GetString("HomePage_NewProject");
    private LocalizedString NewProjectTooltipString => _stringLocalizer.GetString("HomePage_NewProjectTooltip");
    private LocalizedString OpenProjectString => _stringLocalizer.GetString("HomePage_OpenProject");
    private LocalizedString OpenProjectTooltipString => _stringLocalizer.GetString("HomePage_OpenProjectTooltip");
    private LocalizedString NewExampleProjectString => _stringLocalizer.GetString("HomePage_NewExampleProject");
    private LocalizedString NewExampleProjectTooltipString => _stringLocalizer.GetString("HomePage_NewExampleProjectTooltip");
    private LocalizedString RecentString => _stringLocalizer.GetString("HomePage_Recent");

    public HomePageViewModel ViewModel { get; private set; }

    public HomePage()
    {
        this.InitializeComponent();

        ViewModel = ServiceLocator.AcquireService<HomePageViewModel>();

        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
    }

    private void RecentProjectButton_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as HyperlinkButton;
        Guard.IsNotNull(button);

        var recentProject = button.DataContext as RecentProject;
        if (recentProject == null)
        {
            return;
        }

        var projectFilePath = Path.Combine(recentProject.ProjectFolderPath, $"{recentProject.ProjectName}{ProjectConstants.ProjectFileExtension}");
        ViewModel.OpenProject(projectFilePath);
    }
}
