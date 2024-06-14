using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Project.ViewModels;

public partial class ProjectPanelViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;

    [ObservableProperty]
    private string _titleText = string.Empty;

    public ProjectPanelViewModel(
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _messengerService = messengerService;

        // The project data is guaranteed to have been loaded at this point, so it's safe to just
        // acquire a reference via the ProjectService.
        var projectService = workspaceWrapper.WorkspaceService.ProjectService;
        var projectData = projectService.LoadedProjectData;

        TitleText = projectData.ProjectName;
    }

    public ICommand RefreshProjectCommand => new RelayCommand(RefreshProjectCommand_ExecuteAsync);
    private void RefreshProjectCommand_ExecuteAsync()
    {
        var message = new RequestProjectRefreshMessage();
        _messengerService.Send(message);
    }
}
