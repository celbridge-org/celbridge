namespace Celbridge.CommonUI.ViewModels;

public partial class WorkspaceViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    //private readonly IProjectService _projectService;

    [ObservableProperty]
    private bool _isProjectActive;

    public event Action<bool>? WindowActivated;

    public void NotifyWindowActivated(bool active)
    {
        WindowActivated?.Invoke(active);
    }

    public WorkspaceViewModel(IMessengerService messengerService)
    {
        _messengerService = messengerService;
 
        //_messengerService.Register<ActiveProjectChangedMessage>(this, OnActiveProjectChanged);
    }

    /*
    private void OnActiveProjectChanged(object r, ActiveProjectChangedMessage m)
    {
        IsProjectActive = _projectService.ActiveProject != null;
    }
    */
}

