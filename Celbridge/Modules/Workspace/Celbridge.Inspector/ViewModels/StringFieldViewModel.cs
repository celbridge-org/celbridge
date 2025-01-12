using Celbridge.Commands;
using Celbridge.Entities;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using Humanizer;
using System.ComponentModel;

namespace Celbridge.Inspector.ViewModels;

public partial class StringFieldViewModel : ObservableObject
{
    private ILogger<StringFieldViewModel> _logger;
    private IMessengerService _messengerService;
    private ICommandService _commandService;
    private IEntityService _entityService;

    private ComponentKey _componentKey;

    public string PropertyName { get; private set; } = string.Empty;

    [ObservableProperty]
    private string _valueText = string.Empty;

    [ObservableProperty]
    private string _headerText = string.Empty;

    private bool _ignoreComponentChangeMessage = false;
    private bool _ignoreValueChange = false;

    public StringFieldViewModel(
        ILogger<StringFieldViewModel> logger,
        IMessengerService messengerService,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _messengerService = messengerService;
        _commandService = commandService;
        _entityService = workspaceWrapper.WorkspaceService.EntityService;
    }

    public Result Initialize(IComponentProxy component, string propertyName)
    {
        _componentKey = component.Key;

        PropertyName = propertyName;

        // Format the property name header as "Title Case"
        HeaderText = propertyName.Humanize(LetterCasing.Title);

        // Read property into Value
        ReadProperty();

        // Start listening for component changes and value changes
        _messengerService.Register<ComponentChangedMessage>(this, OnComponentChangedMessage);
        PropertyChanged += OnPropertyChanged;

        return Result.Ok();
    }

    private void OnComponentChangedMessage(object recipient, ComponentChangedMessage message)
    {
        if (_ignoreComponentChangeMessage)
        {
            return;
        }

        if (message.ComponentKey == _componentKey &&
            message.PropertyPath == $"/{PropertyName}")
        {
            // _logger.LogInformation("Component changed");
    
            ReadProperty();
        }
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_ignoreValueChange)
        {
            return;
        }

        if (e.PropertyName == nameof(ValueText))
        {
            // _logger.LogInformation("Value changed");

            // Update the property, but ignore the next component change message
            _ignoreComponentChangeMessage = true;
            WriteProperty();
            _ignoreComponentChangeMessage = false;
        }
    }

    private void ReadProperty()
    {
        // Get the component
        var getComponentResult = _entityService.GetComponent(_componentKey);
        if (getComponentResult.IsFailure)
        {
            _logger.LogError(getComponentResult.Error);
            return;
        }
        var component = getComponentResult.Value;

        // Get the property
        var getPropertyResult = component.GetProperty<string>(PropertyName);
        if (getPropertyResult.IsFailure)
        {
            _logger.LogError(getPropertyResult.Error);
            return;
        }
        var stringValue = getPropertyResult.Value;

        _ignoreValueChange = true;
        ValueText = stringValue;
        _ignoreValueChange = false;
    }

    private void WriteProperty()
    {
        // Get the component
        var getComponentResult = _entityService.GetComponent(_componentKey);
        if (getComponentResult.IsFailure)
        {
            _logger.LogError(getComponentResult.Error);
            return;
        }
        var component = getComponentResult.Value;

        _commandService.Execute<ISetPropertyCommand>(command =>
        {
            command.ComponentKey = _componentKey;
            command.PropertyPath = PropertyName;
            command.JsonValue = ValueText;
        });
    }

    public void OnViewUnloaded()
    {
        // Unregister event/message handlers
        _messengerService.UnregisterAll(this);
        PropertyChanged -= OnPropertyChanged;
    }
}
