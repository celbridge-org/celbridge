using Celbridge.BaseLibrary.Messaging;
using CommunityToolkit.Mvvm.Messaging;

namespace Celbridge.Legacy.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly IProjectService _projectService;

    [ObservableProperty]
    private bool _isProjectActive;

    public event Action<bool>? WindowActivated;

    public void NotifyWindowActivated(bool active)
    {
        WindowActivated?.Invoke(active);
    }

    public ShellViewModel(IMessengerService messengerService, IProjectService projectService)
    {
        _messengerService = messengerService;
        _projectService = projectService;

        _messengerService.Register<ActiveProjectChangedMessage>(this, OnActiveProjectChanged);
    }

    private void OnActiveProjectChanged(object r, ActiveProjectChangedMessage m)
    {
        IsProjectActive = _projectService.ActiveProject != null;
    }
}
