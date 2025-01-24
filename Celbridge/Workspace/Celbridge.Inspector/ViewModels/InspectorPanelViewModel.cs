using Celbridge.Explorer;
using Celbridge.Messaging;
using Celbridge.Workspace;
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
        messengerService.Register<SelectedResourceChangedMessage>(this, OnSelectedResourceChangedMessage);
    }

    private void OnSelectedResourceChangedMessage(object recipient, SelectedResourceChangedMessage message)
    {
        SelectedResource = message.Resource;
    }
}
