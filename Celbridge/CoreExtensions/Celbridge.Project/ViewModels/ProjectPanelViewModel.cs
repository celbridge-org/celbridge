using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Project.ViewModels;

public partial class ProjectPanelViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly IUserInterfaceService _userInterfaceService;

    [ObservableProperty]
    private string _titleText = string.Empty;

    public ProjectPanelViewModel(
        IMessengerService messengerService,
        IUserInterfaceService userInterfaceService)
    {
        _messengerService = messengerService;
        _userInterfaceService = userInterfaceService;

        _messengerService.Register<WorkspaceInitializedMessage>(this, OnWorkspaceInitialized);
    }

    private void OnWorkspaceInitialized(object recipient, WorkspaceInitializedMessage message)
    {
        // Todo: Can this be acquired in the constructor instead?
        IProjectService projectService = _userInterfaceService.WorkspaceService.ProjectService;

        var projectData = projectService.LoadedProjectData;
        TitleText = projectData.ProjectName;
    }
}
