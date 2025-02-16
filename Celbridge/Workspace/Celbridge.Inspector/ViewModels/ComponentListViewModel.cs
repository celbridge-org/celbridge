using Celbridge.Activities;
using Celbridge.Commands;
using Celbridge.Entities;
using Celbridge.Forms;
using Celbridge.Inspector.Models;
using Celbridge.Inspector.Services;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Celbridge.Inspector.ViewModels;

public partial class ComponentListViewModel : InspectorViewModel
{
    private readonly ILogger<ComponentListViewModel> _logger;
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IEntityService _entityService;
    private readonly IInspectorService _inspectorService;
    private readonly IActivityService _activityService;
    private readonly IFormService _formService;

    public ObservableCollection<ComponentItem> ComponentItems { get; } = new();

    [ObservableProperty]
    private int _selectedIndex;

    [ObservableProperty]
    private bool _isEditingComponentType;

    private bool _isUpdatePending;

    private Guid? _rootComponentEditorId;
    public event Action<object?>? OnUpdateRootComponentForm;

    // Code gen requires a parameterless constructor
    public ComponentListViewModel()
    {
        throw new NotImplementedException();
    }

    public ComponentListViewModel(
        ILogger<ComponentListViewModel> logger,
        IMessengerService messengerService,
        ICommandService commandService,
        IFormService formService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _messengerService = messengerService;
        _commandService = commandService;
        _formService = formService;
        _entityService = workspaceWrapper.WorkspaceService.EntityService;
        _inspectorService = workspaceWrapper.WorkspaceService.InspectorService;
        _activityService = workspaceWrapper.WorkspaceService.ActivityService;
    }

    public void OnViewLoaded()
    {
        _messengerService.Register<ComponentChangedMessage>(this, OnComponentChangedMessage);
        _messengerService.Register<UpdateInspectorMessage>(this, OnUpdateInspectorMessage);
        _messengerService.Register<UpdatedAnnotationCache>(this, OnUpdatedAnnotationCache);

        PropertyChanged += ViewModel_PropertyChanged;

        // Update the list immediately on startup
        PopulateComponentList();

        // Send a message to populate the component editor in the inspector
        OnPropertyChanged(nameof(SelectedIndex)); 
    }

    public void OnViewUnloaded()
    {
        _messengerService.UnregisterAll(this);

        PropertyChanged -= ViewModel_PropertyChanged;
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

        var executeResult = await _commandService.ExecuteAsync<IAddComponentCommand>(command => 
        {
            command.ComponentKey = new ComponentKey(Resource, addIndex);
            command.ComponentType = EntityConstants.EmptyComponentType;
        });

        if (executeResult.IsFailure)
        {
            // Log the error and refresh the list to attempt to recover
            _logger.LogError(executeResult.Error);
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

        var executeResult = await _commandService.ExecuteAsync<IRemoveComponentCommand>(command =>
        {
            command.ComponentKey = new ComponentKey(Resource, deleteIndex);
        });

        if (executeResult.IsFailure)
        {
            // Log the error and refresh the list to attempt to recover
            _logger.LogError(executeResult.Error);
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

    public ICommand DuplicateComponentCommand => new AsyncRelayCommand<object?>(DuplicateComponent_Executed);
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
            return;
        }

        SelectedIndex = destIndex;
    }

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

        var executeResult = await _commandService.ExecuteAsync<IMoveComponentCommand>(command => { 
            command.Resource = Resource;
            command.SourceComponentIndex = oldIndex;
            command.DestComponentIndex = newIndex;
        });

        if (executeResult.IsFailure)
        {
            // Log the error and refresh the list to attempt to recover
            _logger.LogError(executeResult.Error);
            return;
        }

        // Select the component in its new position
        SelectedIndex = newIndex;
    }

    private void OnComponentChangedMessage(object recipient, ComponentChangedMessage message)
    {
        var resource = message.ComponentKey.Resource;
        var propertyPath = message.PropertyPath;

        if (resource == Resource)
        {
            _isUpdatePending = true;
        }
    }

    private void OnUpdateInspectorMessage(object recipient, UpdateInspectorMessage message)
    {
        if (_isUpdatePending)
        {
            PopulateComponentList();
            _isUpdatePending = false;
        }
    }

    private void OnUpdatedAnnotationCache(object recipient, UpdatedAnnotationCache message)
    {
        UpdateEntityAnnotation();
    }

    private void UpdateEntityAnnotation()
    {
        if (ComponentItems.Count == 0)
        {
            // No components to annotate
            return;
        }

        IEntityAnnotation? entityAnnotation = null;

        var getAnnotationResult = _inspectorService.GetCachedEntityAnnotation(Resource);
        if (getAnnotationResult.IsSuccess)
        {
            entityAnnotation = getAnnotationResult.Value;
        }

        // Apply the most recent annotation data to the corresponding component item
        for (int i = 0; i < ComponentItems.Count; i++)
        {
            var componentItem = ComponentItems[i];
            if (entityAnnotation is not null &&
                i < entityAnnotation.Count)
            {
                componentItem.Annotation = entityAnnotation.GetComponentAnnotation(i);
            }
            else
            {
                componentItem.Annotation = null;
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
        // Note the previous selected index so we can try to set it again at the end
        int previousIndex = SelectedIndex;
        List<ComponentItem> componentItems = new();

        var componentCount = _entityService.GetComponentCount(Resource);

        // Ensure the ComponentItems list is the correct size
        while (ComponentItems.Count < componentCount)
        {
            ComponentItems.Add(new ComponentItem());
        }
        while (ComponentItems.Count > componentCount)
        {
            ComponentItems.RemoveAt(ComponentItems.Count - 1);
        }

        Guid? newRootEditorId = null;
        object? rootComponentForm = null;

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

            if (componentType == EntityConstants.EmptyComponentType)
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

            var componentItem = ComponentItems[i];
            componentItem.ComponentType = componentType;

            // Acquire a component editor for the component
            var componentKey = new ComponentKey(Resource, i);
            var acquireEditorResult = _inspectorService.AcquireComponentEditor(componentKey);
            if (acquireEditorResult.IsFailure)
            {
                _logger.LogError($"Failed to acquire component editor for component: '{componentKey}'");
                continue;
            }
            var editor = acquireEditorResult.Value;

            // Get the component summary from the component editor
            var summary = editor.GetComponentSummary();

            // Populate the component summary
            componentItem.Summary = summary;

            if (i == 0)
            {
                if (editor.EditorId == _rootComponentEditorId)
                {
                    // The root editor instance hasn't changed, so reuse the existing root form.
                    newRootEditorId = _rootComponentEditorId;
                }
                else
                {
                    // The root component editor instance has changed, so refresh the root
                    // component form.

                    // Check if the first component is a root component
                    // Todo: Check that the root component supports this resource type
                    if (editor.Component.IsRootComponent)
                    {
                        // Create a new root form
                        var formConfig = editor.GetComponentRootForm();
                        if (!string.IsNullOrEmpty(formConfig))
                        {
                            var formName = editor.Component.Schema.ComponentType;
                            var createResult = _formService.CreateForm(formName, formConfig, editor);
                            if (createResult.IsSuccess)
                            {
                                rootComponentForm = createResult.Value;
                                newRootEditorId = editor.EditorId;
                            }
                            else
                            {
                                _logger.LogError($"Failed to create root form for root component. {createResult.Error}");
                            }
                        }
                    }
                }
            }
        }

        // Check if the root editor instance has changed
        if (_rootComponentEditorId != newRootEditorId)
        {
            _rootComponentEditorId = newRootEditorId;

            // Notify the view to display the updated root form
            OnUpdateRootComponentForm?.Invoke(rootComponentForm);
        }

        UpdateEntityAnnotation();

        // Select the previous index if it is still valid
        if (componentCount == 0)
        {
            SelectedIndex = -1;
        }
        else
        {
            SelectedIndex = Math.Clamp(previousIndex, 0, componentCount - 1);
        }
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
