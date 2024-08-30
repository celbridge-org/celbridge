using Celbridge.Documents;
using Celbridge.Messaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.StatusBar.ViewModels;

public partial class StatusPanelViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private float _saveIconOpacity;

    public StatusPanelViewModel(IMessengerService messengerService)
    {
        _messengerService = messengerService;
    }

    public void OnLoaded()
    {
        _messengerService.Register<PendingDocumentSaveMessage>(this, OnPendingDocumentSaveMessage);
    }

    public void OnUnloaded()
    {
        _messengerService.Unregister<PendingDocumentSaveMessage>(this);
    }

    private void OnPendingDocumentSaveMessage(object recipient, PendingDocumentSaveMessage message)
    {
        if (message.PendingSaveCount > 0)
        {
            SaveIconOpacity = 1;
        }
        else
        {
            SaveIconOpacity = 0;
        }
    }
}
