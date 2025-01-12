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

        messengerService.Register<InspectedComponentChangedMessage>(this, OnInspectedResourceChangedMessage);
        messengerService.Register<ComponentChangedMessage>(this, OnComponentChangedMessage);
    }

    private void OnInspectedResourceChangedMessage(object recipient, InspectedComponentChangedMessage message)
    {
        var (resource, componentIndex) = message;
        PopulatePropertyList(resource, componentIndex);
    }

    private void OnComponentChangedMessage(object recipient, ComponentChangedMessage message)
    {
        var resource = message.Resource;
        var componentIndex = message.ComponentIndex;
        var propertyPath = message.PropertyPath;

        if (_inspectorService.InspectedResource == resource &&
            propertyPath == "/")
        {
            // We need to repopulate the property list on all structural changes to the entity,
            // because we can't assume that we're inspecting the same component as before, even if 
            // the resource, component index and component type are still the same.
            PopulatePropertyList(resource, componentIndex);
        }
    }

    private void PopulatePropertyList(ResourceKey resource, int componentIndex)
    {
        List<IField> propertyFields = new();

        if (resource.IsEmpty || 
            componentIndex < 0)
        {
            // Setting the inpected item to an invalid resource or invalid component index
            // is expected behaviour that indicates no component is currently being inspected.
            // Clear the UI and return.

            ComponentType = string.Empty;
            OnFormCreated?.Invoke(propertyFields);
            return;
        }

        var componentCount = _entityService.GetComponentCount(resource);

        if (componentCount <= 0 ||
            componentIndex >= componentCount)
        {
            // The inspected component index no longer exists, probably due to a structural change in the entity.
            // Clear the UI and return.

            ComponentType = string.Empty;
            OnFormCreated?.Invoke(propertyFields);
            return;
        }

        // Get the component type
        var getComponentResult = _entityService.GetComponent(resource, componentIndex);
        if (getComponentResult.IsFailure)
        {
            _logger.LogError($"Failed to get component for entity '{resource}' at index '{componentIndex}'");

            ComponentType = string.Empty;
            OnFormCreated?.Invoke(propertyFields);
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
                _logger.LogError($"Failed to create field for property '{property.PropertyName}' for entity '{resource}' at component index {componentIndex}");
                continue;
            }
            var field = createResult.Value;

            propertyFields.Add(field);
        }

        OnFormCreated?.Invoke(propertyFields);
    }
}
