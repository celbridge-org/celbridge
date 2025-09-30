using Celbridge.Messaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Inspector.ViewModels;

public partial class EntityEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private Visibility _componentValueEditorVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private Visibility _componentTypeEditorVisibility = Visibility.Collapsed;

    public event Action? InspectedComponentChanged = null;

    public EntityEditorViewModel(IMessengerService messengerService)
    {
        messengerService.Register<ComponentPanelModeChangedMessage>(this, OnComponentPanelModeChangedMessage);
        messengerService.Register<InspectedComponentChangedMessage>(this, OnInspectedComponentChangedMessage);
    }

    private void OnComponentPanelModeChangedMessage(object recipient, ComponentPanelModeChangedMessage message)
    {
        switch (message.EditMode)
        {
            case ComponentPanelMode.None:
                ComponentValueEditorVisibility = Visibility.Collapsed;
                ComponentTypeEditorVisibility = Visibility.Collapsed;
                break;

            case ComponentPanelMode.ComponentValue:
                ComponentValueEditorVisibility = Visibility.Visible;
                ComponentTypeEditorVisibility = Visibility.Collapsed;
                break;

            case ComponentPanelMode.ComponentType:
                ComponentValueEditorVisibility = Visibility.Collapsed;
                ComponentTypeEditorVisibility = Visibility.Visible;
                break;
        }
    }

    private void OnInspectedComponentChangedMessage(object recipient, InspectedComponentChangedMessage message)
    {
        InspectedComponentChanged?.Invoke();
    }
}
