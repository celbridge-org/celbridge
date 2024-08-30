using Celbridge.Documents;
using Celbridge.Messaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.StatusBar.ViewModels;

public partial class StatusPanelViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;

    [ObservableProperty]
    private string _selectedDocument = string.Empty;

    [ObservableProperty]
    private float _saveIconOpacity;

    public StatusPanelViewModel(IMessengerService messengerService)
    {
        _messengerService = messengerService;
    }

    public void OnLoaded()
    {
        _messengerService.Register<PendingDocumentSaveMessage>(this, OnPendingDocumentSaveMessage);
        _messengerService.Register<SelectedDocumentChangedMessage>(this, OnSelectedDocumentChangedMessage);
    }

    public void OnUnloaded()
    {
        _messengerService.Unregister<PendingDocumentSaveMessage>(this);
        _messengerService.Unregister<SelectedDocumentChangedMessage>(this);
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

    private void OnSelectedDocumentChangedMessage(object recipient, SelectedDocumentChangedMessage message)
    {
        var resource = message.DocumentResource;

        SelectedDocument = resource.ToString();
    }
}
