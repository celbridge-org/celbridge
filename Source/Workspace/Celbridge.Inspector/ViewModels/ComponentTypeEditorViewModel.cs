using Celbridge.Entities;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace Celbridge.Inspector.ViewModels;

public partial class ComponentTypeEditorViewModel : ObservableObject
{
    private readonly ILogger<ComponentValueEditorViewModel> _logger;
    private readonly IEntityService _entityService;
    private readonly IInspectorService _inspectorService;

    [ObservableProperty]
    private ObservableCollection<string> _componentTypeList = new();

    [ObservableProperty]
    private int _selectedIndex = -1;

    public ComponentTypeEditorViewModel(
        ILogger<ComponentValueEditorViewModel> logger,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;

        messengerService.Register<ComponentTypeTextChangedMessage>(this, OnComponentTypeInputTextChangedMessage);
        messengerService.Register<ComponentTypeTextEnteredMessage>(this, OnComponentTypeEnteredMessage);

        _entityService = workspaceWrapper.WorkspaceService.EntityService;
        _inspectorService = workspaceWrapper.WorkspaceService.InspectorService;
    }

    [RelayCommand]
    private void ComponentTypeClicked(string? componentType)
    {
        var componentKey = new ComponentKey(_inspectorService.InspectedResource, _inspectorService.InspectedComponentIndex);

        var getTypeResult = _entityService.GetComponentType(componentKey);
        if (getTypeResult.IsFailure)
        {
            _logger.LogError(getTypeResult.Error);
            return;
        }
        var existingType = getTypeResult.Value;

        // Ensure we are dealing with an empty string if componentType is null
        componentType ??= string.Empty;

        if (existingType == componentType)
        {
            // No change required
            return;
        }

        var replaceResult = _entityService.ReplaceComponent(componentKey, componentType);
        if (replaceResult.IsFailure)
        {
            _logger.LogError(replaceResult.Error);
            return;
        }
    }

    private void OnComponentTypeInputTextChangedMessage(object recipient, ComponentTypeTextChangedMessage message)
    {
        var inputText = message.ComponentType;

        // Get list of available component types
        var componentTypes = _entityService.GetAllComponentTypes();

        var filteredList = new List<string>();

        if (string.IsNullOrEmpty(inputText))
        {
            // Add all component types
            foreach (var name in componentTypes)
            {
                filteredList.Add(name);
            }
        }
        else
        {
            // Add component types that start with the input text first.
            // These should appear at the top of the list.
            foreach (var componentType in componentTypes)
            {
                var dotIndex = componentType.IndexOf('.');
                var unqualifiedComponentType = componentType.Substring(dotIndex);

                if (unqualifiedComponentType.StartsWith(inputText, StringComparison.InvariantCultureIgnoreCase))
                {
                    filteredList.Add(componentType);
                }
            }

            // Remove any component types that have already been added
            componentTypes.Remove(item => filteredList.Contains(item));

            // Todo: Remove any component types that don't allow multiples of the same type (if there's already an instance)

            // Now add any remaining component types that contain the input text
            foreach (var componentType in componentTypes)
            {
                var dotIndex = componentType.IndexOf('.');
                var unqualifiedComponentType = componentType.Substring(dotIndex);

                if (unqualifiedComponentType.Contains(inputText, StringComparison.InvariantCultureIgnoreCase))
                {
                    filteredList.Add(componentType);
                }
            }
        }

        if (filteredList.SequenceEqual(ComponentTypeList))
        {
            return;
        }

        ComponentTypeList.ReplaceWith(filteredList);

        // Force the first item in the component type list to be selected
        SelectedIndex = ComponentTypeList.Count == 0 ? -1 : 0;
        OnPropertyChanged(nameof(SelectedIndex));
    }

    private void OnComponentTypeEnteredMessage(object recipient, ComponentTypeTextEnteredMessage message)
    {
        if (ComponentTypeList.Count == 0)
        {
            // Enter is a noop if no component types are available
            return;
        }

        var newComponentType = ComponentTypeList[0];

        var resource = _inspectorService.InspectedResource;
        var componentIndex = _inspectorService.InspectedComponentIndex;
        var componentKey = new ComponentKey(resource, componentIndex);

        var getTypeResult = _entityService.GetComponentType(componentKey);
        if (getTypeResult.IsFailure)
        {
            _logger.LogError(getTypeResult.Error);
            return;
        }
        var componentType = getTypeResult.Value;

        if (componentType == newComponentType)
        {
            // No change required
            return;
        }

        var replaceResult = _entityService.ReplaceComponent(componentKey, newComponentType);
        if (replaceResult.IsFailure)
        {
            _logger.LogError(replaceResult.Error);
            return;
        }        
    }
}
