using Celbridge.Projects;
using Celbridge.UserInterface.Models;
using Celbridge.UserInterface.ViewModels.Pages;

namespace Celbridge.UserInterface.Views;

public sealed partial class HomePage : Page
{
    private IStringLocalizer _stringLocalizer;

    public LocalizedString TitleString => _stringLocalizer.GetString("HomePage_Title");
    public LocalizedString SubtitleString => _stringLocalizer.GetString("HomePage_Subtitle");
    public LocalizedString StartString => _stringLocalizer.GetString("HomePage_Start");
    public LocalizedString NewProjectString => _stringLocalizer.GetString("HomePage_NewProject");
    public LocalizedString OpenProjectString => _stringLocalizer.GetString("HomePage_OpenProject");
    public LocalizedString CreateExampleProjectString => _stringLocalizer.GetString("HomePage_CreateExampleProject");
    public LocalizedString RecentString => _stringLocalizer.GetString("HomePage_Recent");

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
