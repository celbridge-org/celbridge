using Celbridge.Entities;
using Celbridge.Inspector.Models;
using Celbridge.Inspector.Services;
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

    private bool _pendingRefresh;

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
        messengerService.Register<UpdateInspectorMessage>(this, OnUpdateInspectorMessage);
    }

    private void OnInspectedComponentChangedMessage(object recipient, InspectedComponentChangedMessage message)
    {
        // Repopulate the property list when the selected resource or component changes
        _pendingRefresh = true;
        ClearPropertyList();
    }

    private void OnComponentChangedMessage(object recipient, ComponentChangedMessage message)
    {
        var componentKey = message.ComponentKey;
        var propertyPath = message.PropertyPath;

        if (_inspectorService.InspectedResource == componentKey.Resource &&
            propertyPath == "/")
        {
            // Repopulate the property list on all structural changes to the entity
            _pendingRefresh = true;
            ClearPropertyList();
        }
    }

    private void OnUpdateInspectorMessage(object recipient, UpdateInspectorMessage message)
    {
        if (_pendingRefresh)
        {
            _pendingRefresh = false;
            PopulatePropertyList();
        }
    }

    private void ClearPropertyList()
    {
        ComponentType = string.Empty;
        OnFormCreated?.Invoke(new List<IField>());
    }

    private void PopulatePropertyList()
    {
        var componentKey = new ComponentKey(
            _inspectorService.InspectedResource, 
            _inspectorService.InspectedComponentIndex);
 
        List<IField> propertyFields = new();

        if (componentKey.Resource.IsEmpty ||
            componentKey.ComponentIndex < 0)
        {
            // Setting the inpected item to an invalid resource or invalid component index
            // is expected behaviour that indicates no component is currently being inspected.
            // The list should already be clear, but clear it again just in case.
            ClearPropertyList();
            return;
        }

        var componentCount = _entityService.GetComponentCount(componentKey.Resource);

        if (componentCount <= 0 ||
            componentKey.ComponentIndex >= componentCount)
        {
            // The inspected component index no longer exists, probably due to a structural change in the entity.
            return;
        }

        // Get the component and component type
        var getComponentResult = _entityService.GetComponent(componentKey);
        if (getComponentResult.IsFailure)
        {
            _logger.LogError($"Failed to get component: '{componentKey}'");
            return;
        }
        var component = getComponentResult.Value;

        // Populate the Component Type in the panel header
        ComponentType = component.Schema.ComponentType;

        // Instantiate a ComponentEditor for this component
        var createEditorResult = _entityService.CreateComponentEditor(component);
        if (createEditorResult.IsFailure)
        {
            _logger.LogError($"Failed to create component editor for component type: '{ComponentType}'");
            return;
        }
        var editor = createEditorResult.Value;

        // Instantiate the form UI for the ComponentEditor
        // When the form UI unloads, it will uninitialize the ComponentEditor automatically.
        var createViewResult = _inspectorService.CreateComponentEditorForm(editor);
        if (createViewResult.IsFailure)
        {
            _logger.LogError($"Failed to create component editor view for component type: '{ComponentType}'");
            return;
        }
        var editorView = createViewResult.Value as UIElement;

        if (editorView is not null)
        {
            // Todo: Display the XAML control in the Inspector panel properly, and remove the field system
            var field = new Field(editorView)
    ;       propertyFields.Add(field);
        }


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
