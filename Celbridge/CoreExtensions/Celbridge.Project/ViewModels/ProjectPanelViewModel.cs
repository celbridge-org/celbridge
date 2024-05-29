using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.UserInterface;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Project.ViewModels;

public partial class ProjectPanelViewModel : ObservableObject
{
    IMessengerService _messengerService;
    IProjectService _projectService;

    [ObservableProperty]
    private string _titleText = string.Empty;

    public ProjectPanelViewModel(
        IMessengerService messengerService,
        IUserInterfaceService userInterfaceService,
        IProjectService projectService)
    {
        _messengerService = messengerService;
        _projectService = projectService; // Transient instance created via DI

        // Register the project service with the workspace service
        userInterfaceService.WorkspaceService.RegisterService(_projectService);

        _messengerService.Register<ProjectDataInitializedMessage>(this, OnProjectDataInitialized);
    }

    private void OnProjectDataInitialized(object recipient, ProjectDataInitializedMessage message)
    {
        TitleText = message.ProjectData.ProjectName;
    }
}
