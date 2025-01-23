using Celbridge.Activities;
using Celbridge.Commands;
using Celbridge.Entities;
using Celbridge.Inspector.Models;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Celbridge.Inspector.ViewModels;

public partial class ComponentListViewModel : InspectorViewModel
{
    private const string EmptyComponentType = ".Empty";

    private readonly ILogger<MarkdownInspectorViewModel> _logger;
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IEntityService _entityService;
    private readonly IInspectorService _inspectorService;
    private readonly IActivityService _activityService;

    public ObservableCollection<ComponentItem> ComponentItems { get; } = new();

    [ObservableProperty]
    private int _selectedIndex;

    [ObservableProperty]
    private bool _isEditingComponentType;

    // Code gen requires a parameterless constructor
    public ComponentListViewModel()
    {
        throw new NotImplementedException();
    }

    public ComponentListViewModel(
        ILogger<MarkdownInspectorViewModel> logger,
        IMessengerService messengerService,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _messengerService = messengerService;
        _commandService = commandService;
        _entityService = workspaceWrapper.WorkspaceService.EntityService;
        _inspectorService = workspaceWrapper.WorkspaceService.InspectorService;
        _activityService = workspaceWrapper.WorkspaceService.ActivityService;
    }

    public void OnViewLoaded()
    {
        _messengerService.Register<ComponentChangedMessage>(this, OnComponentChangedMessage);
        _messengerService.Register<ComponentAnnotationUpdatedMessage>(this, OnComponentAnnotationUpdatedMessage);

        PropertyChanged += ViewModel_PropertyChanged;

        PopulateComponentList();

        // Send a message to populate the component editor in the inspector
        OnPropertyChanged(nameof(SelectedIndex)); 
    }

    public void OnViewUnloaded()
    {
        _messengerService.UnregisterAll(this);

        PropertyChanged -= ViewModel_PropertyChanged;
    }

    private void OnComponentAnnotationUpdatedMessage(object recipient, ComponentAnnotationUpdatedMessage message)
    {
        var componentKey = message.ComponentKey;

        if (componentKey.Resource != Resource)
        {
            // This message does not apply to the resource being presented by this ViewModel.
            // This is probably because the user has just switched to inspecting a different resource and the ViewUnloaded
            // callback has not yet been received to clean up this view model.
            return;
        }

        var componentIndex = componentKey.ComponentIndex;
        if (componentIndex < 0 || componentIndex >= ComponentItems.Count)
        {
            // Component index is out of range
            _logger.LogError($"Component index '{componentIndex}' is out of range for resource '{componentKey.Resource}'");
            return;
        }

        var componentItem = ComponentItems[componentIndex];

        var getComponentResult = _entityService.GetComponent(componentKey);
        if (getComponentResult.IsFailure)
        {
            _logger.LogError(getComponentResult.Error);
            return;
        }
        var component = getComponentResult.Value;

        componentItem.Description = component.Description;
        componentItem.Status = component.Status;
        componentItem.Tooltip = component.Tooltip;
    }

    public ICommand AddComponentCommand => new AsyncRelayCommand<object?>(AddComponent_Executed);
    private async Task AddComponent_Executed(object? parameter)
    {
        int addIndex = -1;
        switch (parameter)
        {
            case null:
                // Append to end of list
                addIndex = ComponentItems.Count;
                break;

            case int index:
                // Insert after specified index
                addIndex = index + 1;
                break;

            case ComponentItem componentItem:
                Guard.IsNotNull(componentItem);

                // Insert after specified item
                addIndex = ComponentItems.IndexOf(componentItem) + 1;
                break;
        }

        if (addIndex == -1)
        {
            return;
        }

        // Update the list view
        var emptyComponentItem = new ComponentItem();
        ComponentItems.Insert(addIndex, emptyComponentItem);

        // Supress the refresh
        _supressRefreshCount = 1;

        var executeResult = await _commandService.ExecuteAsync<IAddComponentCommand>(command => 
        {
            command.ComponentKey = new ComponentKey(Resource, addIndex);
            command.ComponentType = EmptyComponentType;
        });

        if (executeResult.IsFailure)
        {
            // Log the error and refresh the list to attempt to recover
            _logger.LogError(executeResult.Error);
            _supressRefreshCount = 0;
            PopulateComponentList();
            return;
        }
    }

    public ICommand DeleteComponentCommand => new AsyncRelayCommand<object?>(DeleteComponent_Executed);
    private async Task DeleteComponent_Executed(object? parameter)
    {
        var deleteIndex = -1;

        switch (parameter)
        {
            case int index:
                deleteIndex = index;
                break;
            case ComponentItem componentItem:
                deleteIndex = ComponentItems.IndexOf(componentItem);
                break;
        }

        if (deleteIndex == -1)
        {
            return;
        }

        // Update the list view
        ComponentItems.RemoveAt(deleteIndex);

        // Supress the refresh
        _supressRefreshCount = 1;


        var executeResult = await _commandService.ExecuteAsync<IRemoveComponentCommand>(command =>
        {
            command.ComponentKey = new ComponentKey(Resource, deleteIndex);
        });

        if (executeResult.IsFailure)
        {
            // Log the error and refresh the list to attempt to recover
            _logger.LogError(executeResult.Error);
            _supressRefreshCount = 0;
            PopulateComponentList();
            return;
        }

        if (ComponentItems.Count == 0)
        {
            // No items available to select
            SelectedIndex = -1;
        }
        else
        {
            // Select the next item at the same index, or the last item if the last item was deleted
            SelectedIndex = Math.Clamp(deleteIndex, 0, ComponentItems.Count - 1);
        }
    }

    public ICommand DuplicateComponentCommand => new AsyncRelayCommand<object?>(DuplicateComponent_Executed, CanDuplicateComponent);
    private async Task DuplicateComponent_Executed(object? parameter)
    {
        var componentItem = parameter as ComponentItem;
        if (componentItem is null)
        {
            return;
        }

        int sourceIndex = ComponentItems.IndexOf(componentItem);
        if (sourceIndex == -1)
        {
            return;
        }

        int destIndex = sourceIndex + 1;

        // Update the list view
        var newItem = new ComponentItem()
        {
            ComponentType = componentItem.ComponentType
        };

        ComponentItems.Insert(destIndex, newItem);

        // Supress the refresh
        _supressRefreshCount = 1;

        var executeResult = await _commandService.ExecuteAsync<ICopyComponentCommand>(command =>
        {
            command.Resource = Resource;
            command.SourceComponentIndex = sourceIndex;
            command.DestComponentIndex = destIndex;
        });

        if (executeResult.IsFailure)
        {
            // Log the error and refresh the list to attempt to recover
            _logger.LogError(executeResult.Error);
            _supressRefreshCount = 0;
            PopulateComponentList();
            return;
        }

        SelectedIndex = destIndex;
    }

    private bool CanDuplicateComponent(object? parameter)
    {
        var componentItem = parameter as ComponentItem;
        if (componentItem is null)
        {
            return false;
        }

        int duplicateIndex = ComponentItems.IndexOf(componentItem);
        if (duplicateIndex == -1)
        {
            return false;
        }

        // Get the component
        var getComponentResult = _entityService.GetComponent(new ComponentKey(Resource, duplicateIndex));
        if (getComponentResult.IsFailure)
        {
            _logger.LogError(getComponentResult.Error);
            return false;
        }
        var component = getComponentResult.Value;

        var allowMultipleComponents = component.Schema.GetBooleanAttribute("allowMultipleComponents");

        return allowMultipleComponents;
    }

    private int _supressRefreshCount;

    public ICommand MoveComponentCommand => new AsyncRelayCommand<object?>(MoveComponent_Executed);
    private async Task MoveComponent_Executed(object? parameter)
    {
        if (parameter is not (int oldIndex, int newIndex))
        {
            throw new InvalidCastException();
        }

        if (oldIndex == newIndex)
        {
            // Item was dragged and dropped at the same index
            return;
        }

        // A move consists of both a remove and add operation, so we need to supress the refresh twice
        _supressRefreshCount = 2;

        var executeResult = await _commandService.ExecuteAsync<IMoveComponentCommand>(command => { 
            command.Resource = Resource;
            command.SourceComponentIndex = oldIndex;
            command.DestComponentIndex = newIndex;
        });

        if (executeResult.IsFailure)
        {
            // Log the error and refresh the list to attempt to recover
            _logger.LogError(executeResult.Error);
            _supressRefreshCount = 0;
            PopulateComponentList();
            return;
        }

        // Select the component in its new position
        SelectedIndex = newIndex;
    }

    private void OnComponentChangedMessage(object recipient, ComponentChangedMessage message)
    {
        if (message.ComponentKey.Resource == Resource &&
            message.PropertyPath == "/")
        {
            // Ignore the requested number of component change messages
            if (_supressRefreshCount > 0)
            {
                _supressRefreshCount--;
            }
            else
            {            
                PopulateComponentList();
            }
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectedIndex))
        {
            var message = new SelectedComponentChangedMessage(SelectedIndex);
            _messengerService.Send(message);

            UpdateEditMode();
        }
        else if (e.PropertyName == nameof(IsEditingComponentType))
        {
            UpdateEditMode();
        }
    }

    private void PopulateComponentList()
    {
        int previousIndex = SelectedIndex;
        List<ComponentItem> componentItems = new();

        var componentCount = _entityService.GetComponentCount(Resource);

        for (int i = 0; i < componentCount; i++)
        {
            // Get the component type
            var getTypeResult = _entityService.GetComponentType(new ComponentKey(Resource, i));
            if (getTypeResult.IsFailure)
            {
                _logger.LogError(getTypeResult.Error);
                return;
            }
            var componentType = getTypeResult.Value;

            if (componentType == EmptyComponentType)
            {
                componentType = string.Empty;
            }
            else
            {
                // Remove the namespace part of the component type
                var dotIndex = componentType.LastIndexOf('.');
                if (dotIndex >= 0)
                {
                    componentType = componentType.Substring(dotIndex + 1);
                }
            }

            var componentItem = new ComponentItem
            {
                ComponentType = componentType
            };

            componentItems.Add(componentItem);
        }

        ComponentItems.ReplaceWith(componentItems);

        if (componentCount == 0)
        {
            SelectedIndex = -1;
        }
        else
        {
            SelectedIndex = Math.Clamp(previousIndex, 0, componentCount - 1);
        }

        // Notify running activities that the component list has been populated, so that
        // they may now add annotations to present component information in the inspector.

        var message = new PopulatedComponentListMessage(Resource);
        _messengerService.Send(message);
    }

    private void UpdateEditMode()
    {
        var mode = ComponentPanelMode.None;

        if (SelectedIndex > -1)
        {
            if (IsEditingComponentType)
            {
                mode = ComponentPanelMode.ComponentType;
            }
            else
            {
                mode = ComponentPanelMode.ComponentValue;
            }
        }

        _inspectorService.ComponentPanelMode = mode;

        if (mode == ComponentPanelMode.ComponentType)
        {
            UpdateComponentTypeInput();
        }
    }

    private void UpdateComponentTypeInput()
    {
        if (SelectedIndex < 0 || SelectedIndex >= ComponentItems.Count)
        {
            return;
        }

        var componentType = ComponentItems[SelectedIndex].ComponentType;

        NotifyComponentTypeTextChanged(componentType);
    }

    public void NotifyComponentTypeTextChanged(string inputText)
    {
        var message = new ComponentTypeTextChangedMessage(inputText);
        _messengerService.Send(message);
    }

    public void NotifyComponentTypeTextEntered()
    {
        var message = new ComponentTypeTextEnteredMessage();
        _messengerService.Send(message);
    }
}
