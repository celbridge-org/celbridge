namespace Celbridge.CommonUI.ViewModels;

public partial class WorkspaceViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;

    public WorkspaceViewModel(IMessengerService messengerService)
    {
        _messengerService = messengerService;
    }
}

