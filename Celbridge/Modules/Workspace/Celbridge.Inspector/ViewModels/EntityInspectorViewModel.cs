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

public partial class EntityInspectorViewModel : InspectorViewModel
{
    private readonly ILogger<MarkdownInspectorViewModel> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IEntityService _entityService;

    public ObservableCollection<ComponentItem> ComponentItems { get; } = new();

    [ObservableProperty]
    private int _selectedComponentIndex;

    // Code gen requires a parameterless constructor
    public EntityInspectorViewModel()
    {
        throw new NotImplementedException();
    }

    public EntityInspectorViewModel(
        ILogger<MarkdownInspectorViewModel> logger,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _messengerService = messengerService;
        _entityService = workspaceWrapper.WorkspaceService.EntityService;

        _messengerService.Register<ComponentChangedMessage>(this, OnComponentChangedMessage);

        PropertyChanged += EntityInspectorViewModel_PropertyChanged;
    }

    public void OnViewLoaded()
    {
        PopulateComponentList();
    }

    public ICommand AddComponentCommand => new RelayCommand<object?>(AddComponent_Executed);
    private void AddComponent_Executed(object? parameter)
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

        var addResult = _entityService.AddComponent(Resource, "Empty", addIndex);
        if (addResult.IsFailure)
        {
            // Log the error and refresh the list to attempt to recover
            _logger.LogError(addResult.Error);
            _supressRefreshCount = 0;
            PopulateComponentList();
            return;
        }
    }

    public ICommand DeleteComponentCommand => new RelayCommand<object?>(DeleteComponent_Executed);
    private void DeleteComponent_Executed(object? parameter)
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

        var deleteResult = _entityService.RemoveComponent(Resource, deleteIndex);
        if (deleteResult.IsFailure)
        {
            // Log the error and refresh the list to attempt to recover
            _logger.LogError(deleteResult.Error);
            _supressRefreshCount = 0;
            PopulateComponentList();
            return;
        }
    }

    public ICommand DuplicateComponentCommand => new RelayCommand<object?>(DuplicateComponent_Executed, CanDuplicateComponent);
    private void DuplicateComponent_Executed(object? parameter)
    {
        var componentItem = parameter as ComponentItem;
        if (componentItem is null)
        {
            return;
        }

        int duplicateIndex = ComponentItems.IndexOf(componentItem);
        if (duplicateIndex == -1)
        {
            return;
        }

        // Update the list view
        var item = ComponentItems[duplicateIndex];
        ComponentItems.Insert(duplicateIndex + 1, item);

        // Supress the refresh
        _supressRefreshCount = 1;

        var copyResult = _entityService.CopyComponent(Resource, duplicateIndex, duplicateIndex + 1);
        if (copyResult.IsFailure)
        {
            // Log the error and refresh the list to attempt to recover
            _logger.LogError(copyResult.Error);
            _supressRefreshCount = 0;
            PopulateComponentList();
            return;
        }
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

        var getResults = _entityService.GetComponentInfo(Resource, duplicateIndex);
        if (getResults.IsFailure)
        {
            _logger.LogError(getResults.Error);
            return false;
        }

        var componentInfo = getResults.Value;

        var allowMultipleComponents = componentInfo.GetBooleanAttribute("allowMultipleComponents");

        return allowMultipleComponents;
    }

    private int _supressRefreshCount;

    public ICommand MoveComponentCommand => new RelayCommand<object?>(MoveComponent_Executed);
    private void MoveComponent_Executed(object? parameter)
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
        var moveResult = _entityService.MoveComponent(Resource, oldIndex, newIndex);
        if (moveResult.IsFailure)
        {
            // Log the error and refresh the list to attempt to recover
            _logger.LogError(moveResult.Error);
            _supressRefreshCount = 0;
            PopulateComponentList();
            return;
        }
    }

    private void OnComponentChangedMessage(object recipient, ComponentChangedMessage message)
    {
        if (message.Resource == Resource &&
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

    private void EntityInspectorViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Resource))
        {
            // Todo: Update component list
        }
    }

    private void PopulateComponentList()
    {
        var getCountResult = _entityService.GetComponentCount(Resource);
        if (getCountResult.IsFailure)
        {
            _logger.LogError(getCountResult.Error);
            return;
        }
        var count = getCountResult.Value;

        int previousIndex = SelectedComponentIndex;

        List<ComponentItem> componentItems = new();
        for (int i = 0; i < count; i++)
        {
            var getComponentResult = _entityService.GetComponentInfo(Resource, i);
            if (getComponentResult.IsFailure)
            {
                _logger.LogError(getComponentResult.Error);
                return;
            }
            var componentInfo = getComponentResult.Value;

            var componentType = componentInfo.ComponentType;
            if (componentType == "Empty")
            {
                componentType = string.Empty;
            }

            var componentItem = new ComponentItem
            {
                ComponentType = componentType
            };

            componentItems.Add(componentItem);
        }

        ComponentItems.ReplaceWith(componentItems);

        if (count == 0)
        {
            SelectedComponentIndex = -1;
        }
        else
        {
            SelectedComponentIndex = Math.Clamp(previousIndex, 0, count - 1);
        
        }
    }
}
