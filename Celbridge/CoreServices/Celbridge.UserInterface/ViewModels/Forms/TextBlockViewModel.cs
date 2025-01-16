using Celbridge.Entities;
using Celbridge.Forms;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.ViewModels.Forms;

public partial class TextBlockViewModel : ObservableObject, IFormElementViewModel
{
    private readonly IMessengerService _messengerService;
    private readonly IEntityService _entityService;

    [ObservableProperty]
    private string _text = string.Empty;

    private PropertyBinding? _textBinding;

    public TextBlockViewModel(
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _messengerService = messengerService;
        _entityService = workspaceWrapper.WorkspaceService.EntityService;
    }

    public void Bind(PropertyBinding binding)
    {
        // Store the component key and property path
        _textBinding = binding;

        // Listen for component changes and update the text
        _messengerService.UnregisterAll(this);
        _messengerService.Register<ComponentChangedMessage>(this, OnComponentChangedMessage);

        // Get the property when initializing the binding
        UpdateProperty();

        // Todo: Log binding errors - display empty text in error state
    }

    public void Unbind()
    {
        _messengerService.UnregisterAll(this);
        _textBinding = null;
    }

    private void OnComponentChangedMessage(object recipient, ComponentChangedMessage message)
    {
        Guard.IsNotNull(_textBinding);

        if (message.ComponentKey == _textBinding.ComponentKey && 
            message.PropertyPath == _textBinding.PropertyPath)
        {
            UpdateProperty();
        }
    }

    private void UpdateProperty()
    {
        Guard.IsNotNull(_textBinding);

        var getComponentResult = _entityService.GetComponent(_textBinding.ComponentKey);
        if (getComponentResult.IsFailure)
        {
            // Todo: Log error
            return;
        }
        var component = getComponentResult.Value;

        Text = component.GetString(_textBinding.PropertyPath);
    }
}
