using Celbridge.Documents;
using Celbridge.Navigation;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.UserInterface.ViewModels.Controls;

public partial class TitleBarViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly INavigationService _navigationService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

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
        IWorkspaceWrapper workspaceWrapper)
    {
        _messengerService = messengerService;
        _navigationService = navigationService;
        _workspaceWrapper = workspaceWrapper;
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
    }

    private void OnWorkspaceUnloaded(object recipient, WorkspaceUnloadedMessage message)
    {
        OnPropertyChanged(nameof(IsWorkspaceLoaded));
    }

    /// <summary>
    /// Called when a navigation item is selected in the TitleBar navigation.
    /// Routes to the appropriate navigation service method.
    /// </summary>
    public void OnNavigationItemSelected(string tag)
    {
        _navigationService.NavigateByTag(tag);
    }
}


