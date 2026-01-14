using Celbridge.Documents;
using Celbridge.Navigation;
using Celbridge.Projects;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.UserInterface.ViewModels.Controls;

/// <summary>
/// ViewModel for the TitleBar control, handling navigation between top-level pages.
/// </summary>
public partial class TitleBarViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly INavigationService _navigationService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectService _projectService;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private bool _isWorkspaceActive;

    [ObservableProperty]
    private string _projectTitle = string.Empty;

    public bool IsWorkspaceLoaded => _workspaceWrapper.IsWorkspacePageLoaded;

    public TitleBarViewModel(
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
        _messengerService.Register<WorkspacePageActivatedMessage>(this, OnWorkspacePageActivated);
        _messengerService.Register<WorkspacePageDeactivatedMessage>(this, OnWorkspacePageDeactivated);
        _messengerService.Register<PendingDocumentSaveMessage>(this, OnPendingDocumentSaveMessage);
        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoaded);
        _messengerService.Register<WorkspaceUnloadedMessage>(this, OnWorkspaceUnloaded);
    }

    public void OnUnloaded()
    {
        _messengerService.UnregisterAll(this);
    }

    private void OnWorkspacePageActivated(object recipient, WorkspacePageActivatedMessage message)
    {
        IsWorkspaceActive = true;
    }

    private void OnWorkspacePageDeactivated(object recipient, WorkspacePageDeactivatedMessage message)
    {
        IsWorkspaceActive = false;
    }

    private void OnPendingDocumentSaveMessage(object recipient, PendingDocumentSaveMessage message)
    {
        IsSaving = message.PendingSaveCount > 0;
    }

    private void OnWorkspaceLoaded(object recipient, WorkspaceLoadedMessage message)
    {
        OnPropertyChanged(nameof(IsWorkspaceLoaded));
        UpdateProjectTitle();
    }

    private void OnWorkspaceUnloaded(object recipient, WorkspaceUnloadedMessage message)
    {
        OnPropertyChanged(nameof(IsWorkspaceLoaded));
        ProjectTitle = string.Empty;
    }

    private void UpdateProjectTitle()
    {
        var currentProject = _projectService.CurrentProject;
        if (currentProject != null)
        {
            ProjectTitle = currentProject.ProjectName;
        }
        else
        {
            ProjectTitle = string.Empty;
        }
    }

    /// <summary>
    /// Navigates to a top-level page using the navigation tag.
    /// </summary>
    public void NavigateToPage(string tag)
    {
        _navigationService.NavigateToPage(tag);
    }
}


