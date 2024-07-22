using Celbridge.Messaging;

namespace Celbridge.Legacy.ViewModels;

public partial class StatusBarViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;

    [ObservableProperty]
    private bool _isSaving;

    public StatusBarViewModel(IMessengerService messengerService) 
    {
        _messengerService = messengerService;

        _messengerService.Register<SaveQueueUpdatedMessage>(this, OnSaveQueueUpdated);
    }

    private void OnSaveQueueUpdated(object recipient, SaveQueueUpdatedMessage message)
    {
        var pendingSaveCount = message.PendingSaveCount;
        if (pendingSaveCount == 0)
        {
            IsSaving = false;
        }
        else
        {
            IsSaving = true;
        }
    }
}
