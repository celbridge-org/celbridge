using Celbridge.Entities;
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

    public event Action<List<UIElement>>? OnFormCreated;

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
        OnFormCreated?.Invoke(new List<UIElement>());
    }

    private void PopulatePropertyList()
    {
        var componentKey = new ComponentKey(
            _inspectorService.InspectedResource, 
            _inspectorService.InspectedComponentIndex);
 
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

        // Acquire a ComponentEditor for this component
        var acquireEditorResult = _inspectorService.AcquireComponentEditor(componentKey);
        if (acquireEditorResult.IsFailure)
        {
            _logger.LogError(acquireEditorResult.Error);
            _logger.LogError($"Failed to acquire component editor for component: '{componentKey}'");
            return;
        }
        var editor = acquireEditorResult.Value;

        // Instantiate the form UI for the ComponentEditor
        // When the form UI unloads, it will uninitialize the ComponentEditor automatically.
        var createViewResult = _inspectorService.CreateComponentEditorForm(editor);
        if (createViewResult.IsFailure)
        {
            _logger.LogError(createViewResult.Error);
            _logger.LogError($"Failed to create component editor view for component type: '{ComponentType}'");
            return;
        }
        var uiElement = createViewResult.Value as UIElement;

        if (uiElement is not null)
        {
            // Display the form UI in the inspector panel
            // Using a list here avoids having to pass a nullable parameter to clear the list. Also handy if
            // we want to insert additional UI elements in the future.
            var uiElements = new List<UIElement>() 
            { 
                uiElement 
            };
            OnFormCreated?.Invoke(uiElements);
        }    
    }
}
