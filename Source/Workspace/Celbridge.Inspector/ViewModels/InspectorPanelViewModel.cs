using Celbridge.Documents;
using Celbridge.Messaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Inspector.ViewModels;

public partial class InspectorPanelViewModel : ObservableObject
{
    [ObservableProperty]
    private ResourceKey _selectedResource;

    public InspectorPanelViewModel()
    {
        throw new NotImplementedException();
    }

    public InspectorPanelViewModel(IMessengerService messengerService)
    {
        messengerService.Register<SelectedDocumentChangedMessage>(this, OnSelectedDocumentChangedMessage);
    }

    private void OnSelectedDocumentChangedMessage(object recipient, SelectedDocumentChangedMessage message)
    {
        if (SelectedResource == message.DocumentResource)
        {
            // Avoid unnecessary UI rebuild when the same document is re-selected
            return;
        }

        SelectedResource = message.DocumentResource;
    }
}
