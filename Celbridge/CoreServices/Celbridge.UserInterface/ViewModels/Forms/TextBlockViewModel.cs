using Celbridge.Entities;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.ViewModels.Forms;

public partial class TextBlockViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly IEntityService _entityService;

    [ObservableProperty]
    private string _text = string.Empty;

    public ComponentKey Component { get; set; }

    public PropertyBinding? TextBinding { get; set; }

    public TextBlockViewModel(
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _messengerService = messengerService;
        _entityService = workspaceWrapper.WorkspaceService.EntityService;
    }

    public void Bind()
    {
        // Listen for component changes and update the text
        Unbind();
        _messengerService.Register<ComponentChangedMessage>(this, OnComponentChangedMessage);

        // Get the property when initializing the binding
        UpdateProperty();
    }

    public void Unbind()
    {
        _messengerService.UnregisterAll(this);
    }

    private void OnComponentChangedMessage(object recipient, ComponentChangedMessage message)
    {
        if (message.ComponentKey == Component)
        {
            UpdateProperty();
        }
    }

    private void UpdateProperty()
    {
        var getComponentResult = _entityService.GetComponent(Component);
        if (getComponentResult.IsFailure)
        {
            // Todo: Log error
            return;
        }
        var component = getComponentResult.Value;

        if (TextBinding is not null)
        {
            Text = component.GetString(TextBinding.PropertyPath);
        }
    }
}
