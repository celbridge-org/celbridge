using Celbridge.Documents;
using Celbridge.Navigation;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.ViewModels.Controls;

/// <summary>
/// ViewModel for the TitleBar control.
/// </summary>
public partial class TitleBarViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private bool _isWorkspaceActive;

    public TitleBarViewModel(
        IMessengerService messengerService,
        INavigationService navigationService)
    {
        _messengerService = messengerService;
        _navigationService = navigationService;
    }

    public void OnLoaded()
    {
        _messengerService.Register<WorkspacePageActivatedMessage>(this, OnWorkspacePageActivated);
        _messengerService.Register<WorkspacePageDeactivatedMessage>(this, OnWorkspacePageDeactivated);
        _messengerService.Register<PendingDocumentSaveMessage>(this, OnPendingDocumentSaveMessage);
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

    /// <summary>
    /// Navigates to a top-level page using the navigation tag.
    /// </summary>
    public void NavigateToPage(string tag)
    {
        _navigationService.NavigateToPage(tag);
    }
}


