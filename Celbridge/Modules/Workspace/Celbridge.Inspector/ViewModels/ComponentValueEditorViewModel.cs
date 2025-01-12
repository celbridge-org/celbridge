using Celbridge.Entities;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Inspector.ViewModels;

public partial class ComponentValueEditorViewModel : ObservableObject
{
    private readonly ILogger<ComponentValueEditorViewModel> _logger;
    private readonly IEntityService _entityService;
    private readonly IInspectorService _inspectorService;

    [ObservableProperty]
    private string _componentType = string.Empty;

    public event Action<List<IField>>? OnFormCreated;

    public ComponentValueEditorViewModel(
        ILogger<ComponentValueEditorViewModel> logger,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _entityService = workspaceWrapper.WorkspaceService.EntityService;
        _inspectorService = workspaceWrapper.WorkspaceService.InspectorService;

        messengerService.Register<InspectedComponentChangedMessage>(this, OnInspectedComponentChangedMessage);
        messengerService.Register<ComponentChangedMessage>(this, OnComponentChangedMessage);
    }

    private void OnInspectedComponentChangedMessage(object recipient, InspectedComponentChangedMessage message)
    {
        ClearPropertyList();
        PopulatePropertyList(message.ComponentKey);
    }

    private void OnComponentChangedMessage(object recipient, ComponentChangedMessage message)
    {
        var componentKey = message.ComponentKey;
        var propertyPath = message.PropertyPath;

        if (_inspectorService.InspectedResource == componentKey.Resource &&
            propertyPath == "/")
        {
            // We need to repopulate the property list on all structural changes to the entity,
            // because we can't assume that we're inspecting the same component as before, even if 
            // the resource, component index and component type are still the same.
            ClearPropertyList();
            PopulatePropertyList(componentKey);
        }
    }

    private void ClearPropertyList()
    {
        ComponentType = string.Empty;
        OnFormCreated?.Invoke(new List<IField>());
    }

    private void PopulatePropertyList(ComponentKey componentKey)
    {
        List<IField> propertyFields = new();

        if (componentKey.Resource.IsEmpty ||
            componentKey.ComponentIndex < 0)
        {
            // Setting the inpected item to an invalid resource or invalid component index
            // is expected behaviour that indicates no component is currently being inspected.
            return;
        }

        var componentCount = _entityService.GetComponentCount(componentKey.Resource);

        if (componentCount <= 0 ||
            componentKey.ComponentIndex >= componentCount)
        {
            // The inspected component index no longer exists, probably due to a structural change in the entity.
            return;
        }

        // Get the component type
        var getComponentResult = _entityService.GetComponent(componentKey);
        if (getComponentResult.IsFailure)
        {
            _logger.LogError($"Failed to get component: '{componentKey}'");
            return;
        }
        var component = getComponentResult.Value;

        // Populate the Component Type in the panel header

        ComponentType = component.Schema.ComponentType;

        // Construct the form by adding property fields one by one.

        foreach (var property in component.Schema.Properties)
        {
            var createResult = _inspectorService.FieldFactory.CreatePropertyField(component, property.PropertyName);
            if (createResult.IsFailure)
            {
                _logger.LogError($"Failed to create field for property '{property.PropertyName}' for component '{componentKey}'");
                continue;
            }
            var field = createResult.Value;

            propertyFields.Add(field);
        }

        OnFormCreated?.Invoke(propertyFields);
    }
}
