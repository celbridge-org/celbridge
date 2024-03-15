using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.UserInterface;

namespace Celbridge.Project.ViewModels;

public class ProjectPanelViewModel
{
    IProjectService _projectService;

    public ProjectPanelViewModel(
        IUserInterfaceService userInterfaceService,
        IProjectService projectService)
    {
        _projectService = projectService; // Transient instance created via DI

        // Register the project service with the workspace service
        userInterfaceService.WorkspaceService.RegisterService(_projectService);
    }
}
