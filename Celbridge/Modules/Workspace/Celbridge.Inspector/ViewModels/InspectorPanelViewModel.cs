using Celbridge.Explorer;
using Celbridge.Messaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Inspector.ViewModels;

public partial class InspectorPanelViewModel : ObservableObject
{
    [ObservableProperty]
    private ResourceKey _selectedResource;

    public InspectorPanelViewModel()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;
        var messengerService = serviceProvider.GetRequiredService<IMessengerService>();

        messengerService.Register<SelectedResourceChangedMessage>(this, OnSelectedResourceChangedMessage);
    }

    private void OnSelectedResourceChangedMessage(object recipient, SelectedResourceChangedMessage message)
    {
        SelectedResource = message.Resource;
    }
}
