using Celbridge.Navigation;
using Celbridge.Projects;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.ViewModels.Controls;

public partial class PageNavigationToolbarViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly INavigationService _navigationService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectService _projectService;

    [ObservableProperty]
    private string _projectTitle = string.Empty;

    [ObservableProperty]
    private string _projectFilePath = string.Empty;

    public bool IsWorkspaceLoaded => _workspaceWrapper.IsWorkspacePageLoaded;

    public PageNavigationToolbarViewModel(
        IMessengerService messengerService,
        INavigationService navigationService,
        IWorkspaceWrapper workspaceWrapper,
        IProjectService projectService)
    {
        _messengerService = messengerService;
        _navigationService = navigationService;
        _workspaceWrapper = workspaceWrapper;
        _projectService = projectService;
    }

    public void OnLoaded()
    {
        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoaded);
        _messengerService.Register<WorkspaceUnloadedMessage>(this, OnWorkspaceUnloaded);
    }

    public void OnUnloaded()
    {
        _messengerService.UnregisterAll(this);
    }

    private void OnWorkspaceLoaded(object recipient, WorkspaceLoadedMessage message)
    {
        OnPropertyChanged(nameof(IsWorkspaceLoaded));
        UpdateProjectInfo();
    }

    private void OnWorkspaceUnloaded(object recipient, WorkspaceUnloadedMessage message)
    {
        OnPropertyChanged(nameof(IsWorkspaceLoaded));
        ProjectTitle = string.Empty;
        ProjectFilePath = string.Empty;
    }

    private void UpdateProjectInfo()
    {
        var currentProject = _projectService.CurrentProject;
        if (currentProject != null)
        {
            ProjectTitle = currentProject.ProjectName;
            ProjectFilePath = currentProject.ProjectFilePath;
        }
        else
        {
            ProjectTitle = string.Empty;
            ProjectFilePath = string.Empty;
        }
    }

    public void NavigateToPage(string tag)
    {
        _navigationService.NavigateToPage(tag);
    }
}
