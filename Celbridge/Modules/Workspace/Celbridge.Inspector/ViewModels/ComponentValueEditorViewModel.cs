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
            // Clear the UI and return

            ComponentType = string.Empty;
            OnFormCreated?.Invoke(propertyFields);
            return;
        }

        // The resource and component index appear to be valid, so we can attempt to get information
        // about the component. If these queries fail, log the error and clear the UI.

        var getCountResult = _entityService.GetComponentCount(resource);
        if (getCountResult.IsFailure)
        {
            _logger.LogError($"Failed to get component count for resource: '{resource}'");

            ComponentType = string.Empty;
            OnFormCreated?.Invoke(propertyFields);
            return;
        }

        int componentCount = getCountResult.Value;
        if (componentIndex >= componentCount)
        {
            _logger.LogError($"Component index '{componentIndex}' is out of range for resource '{resource}'");

            ComponentType = string.Empty;
            OnFormCreated?.Invoke(propertyFields);
            return;
        }

        var getResult = _entityService.GetComponentTypeInfo(resource, componentIndex);
        if (getResult.IsFailure)
        {
            _logger.LogError($"Failed to get component type info for resource '{resource}' at index '{componentIndex}'");

            ComponentType = string.Empty;
            OnFormCreated?.Invoke(propertyFields);
            return;
        }

        // Populate the Component Type in the panel header

        var componentTypeInfo = getResult.Value;
        ComponentType = componentTypeInfo.ComponentType;

        // Construct the form by adding property fields one by one.

        foreach (var property in componentTypeInfo.Properties)
        {
            var createResult = _inspectorService.FieldFactory.CreatePropertyField(resource, componentIndex, property.PropertyName);
            if (createResult.IsFailure)
            {
                _logger.LogError($"Failed to create field for property '{property.PropertyName}' in resource '{resource}' at component index '{componentIndex}'");
                continue;
            }
            var field = createResult.Value;

            propertyFields.Add(field);
        }

        OnFormCreated?.Invoke(propertyFields);
    }
}
