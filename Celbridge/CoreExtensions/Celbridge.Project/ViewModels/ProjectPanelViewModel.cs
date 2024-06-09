using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.UserInterface;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Project.ViewModels;

public partial class ProjectPanelViewModel : ObservableObject
{
    private readonly IUserInterfaceService _userInterfaceService;

    [ObservableProperty]
    private string _titleText = string.Empty;

    public ProjectPanelViewModel(
        IUserInterfaceService userInterfaceService)
    {
        _userInterfaceService = userInterfaceService;

        // The project data is guaranteed to have been loaded at this point, so it's safe to just
        // acquire a reference via the ProjectService.
        var projectService = _userInterfaceService.WorkspaceService.ProjectService;
        var projectData = projectService.LoadedProjectData;

        TitleText = projectData.ProjectName;
    }
}
