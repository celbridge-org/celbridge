using Celbridge.Entities;
using Celbridge.Explorer;
using Celbridge.Inspector;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;

using Path = System.IO.Path;

namespace Celbridge.Activities.Services;

public class ActivityDispatcher
{
    private readonly ILogger<ActivityDispatcher> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IEntityService _entityService;

    private ActivityRegistry? _activityRegistry;

    private HashSet<ResourceKey> _pendingEntityUpdates = new();

    public ActivityDispatcher(
        ILogger<ActivityDispatcher> logger,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;

        _messengerService = messengerService;
        _entityService = workspaceWrapper.WorkspaceService.EntityService;
    }

    public async Task<Result> Initialize(ActivityRegistry activityRegistry)
    {
        _activityRegistry = activityRegistry;

        _messengerService.Register<EntityCreatedMessage>(this, OnEntityCreatedMessage);

        _messengerService.Register<SelectedResourceChangedMessage>(this, (s, e) => HandleMessage(e.Resource));
        _messengerService.Register<ComponentChangedMessage>(this, (s, e) => HandleMessage(e.Resource));
        _messengerService.Register<PopulatedComponentListMessage>(this, (s, e) => HandleMessage(e.Resource));

        void HandleMessage(ResourceKey resource)
        {
            _pendingEntityUpdates.Add(resource);
        }

        await Task.CompletedTask;

        return Result.Ok();
    }

    private void OnEntityCreatedMessage(object recipient, EntityCreatedMessage message)
    {
        Guard.IsNotNull(_activityRegistry);

        var resource = message.Resource;

        var fileExtension = Path.GetExtension(resource.ToString());
        var hasExtension = !string.IsNullOrEmpty(fileExtension);

        if (hasExtension)
        {
            // Try each activity in alphabetic order for deterministic results
            var keys = _activityRegistry.Activities.Keys.ToList();
            keys.Sort();

            foreach (var key in keys)
            {
                // Attempt to initialize the entity with this activity
                var activity = _activityRegistry.Activities[key];
                if (activity.TryInitializeEntity(resource))
                {
                    // First activity to initialize the entity wins
                    break;
                }
            }
        }
    }

    public void Uninitialize()
    {
        _messengerService.Unregister<EntityCreatedMessage>(this);
        _messengerService.Unregister<SelectedResourceChangedMessage>(this);
        _messengerService.Unregister<ComponentChangedMessage>(this);
        _messengerService.Unregister<PopulatedComponentListMessage>(this);
    }

    public async Task<Result> UpdateAsync()
    {
        Guard.IsNotNull(_activityRegistry);

        foreach (var fileResource in _pendingEntityUpdates)
        {
            // Get the component list for this entity
            var getComponentsResult = _entityService.GetComponents(fileResource);
            if (getComponentsResult.IsFailure)
            {
                return Result.Fail($"Failed to get components for entity: '{fileResource}'")
                    .WithErrors(getComponentsResult);
            }
            var components = getComponentsResult.Value;

            var unprocessedComponents = new List<int>();

            // Annotate empty components

            for (int i = 0; i < components.Count; i++)
            {
                var component = components[i];

                if (component.Schema.ComponentType != "Empty")
                {
                    unprocessedComponents.Add(i);
                    continue;
                }

                AnnotateEmptyComponent(component);
            }

            // Check that the entity has a Primary Component

            bool hasPrimaryComponent = _entityService.HasTag(fileResource, "PrimaryComponent");
            if (!hasPrimaryComponent)
            {
                // Move onto next modified file resource
                continue;
            }

            // Check that there is at least one component.
            // There must be if the PrimaryComponent tag is present.

            var getCountResult = _entityService.GetComponentCount(fileResource);
            if (getCountResult.IsFailure)
            {
                return Result.Fail($"Failed to get component count for entity: '{fileResource}'");
            }
            var componentCount = getCountResult.Value;

            if (componentCount == 0)
            {
                return Result.Fail($"Component count is zero for entity: '{fileResource}'");
            }

            // Get the schema for the Primary Component

            IComponentProxy? primaryComponent = null;

            bool syntaxError = false;

            foreach (var componentIndex in unprocessedComponents)
            {
                var component = components[componentIndex];

                if (component.Schema.ComponentType == "Empty")
                {
                    // Ignore empty components at top of Entity
                    continue;
                }

                if (component.Schema.HasTag("PrimaryComponent"))
                {
                    if (primaryComponent is not null)
                    {
                        // Todo: Annotate the component with an error message
                        // Multiple Primary Components detected
                        continue;
                    }

                    // Found the Primary Component
                    primaryComponent = component;
                }
                else
                {
                    if (primaryComponent is null)
                    {
                        component.SetAnnotation(
                            ComponentStatus.Error, 
                            "Invalid component position", 
                            "This component must be placed after the Primary Component");
                        syntaxError = true;

                        continue;
                    }
                }
            }
        
            if (primaryComponent is null)
            {
                syntaxError = true;
                continue;
            }

            if (syntaxError) 
            {
                // Todo: Annotate all components with an error message
                // Invalid component
                // No Primary Component defined

                continue;
            }

            // Get the activity name for this Primary Component

            var activityName = primaryComponent.Schema.GetStringAttribute("activityName");
            if (string.IsNullOrEmpty(activityName))
            {
                return Result.Fail($"Activity name is empty for Primary Component on resource: {fileResource}");
            }

            if (!_activityRegistry.Activities.TryGetValue(activityName, out var activity))
            {
                // Todo: Annotate the component with an error message
                continue;
            }

            // Todo: Check that the Primary Component is allowed for this resource type

            // Use the activity to annotate all components in the entity

            var updateResult = await activity.UpdateEntityAsync(fileResource);
            if (updateResult.IsFailure)
            {
                return Result.Fail($"Failed to update resource: '{fileResource}'")
                    .WithErrors(updateResult);
            }
        }

        _pendingEntityUpdates.Clear();

        await Task.CompletedTask;

        return Result.Ok();
    }

    private void AnnotateEmptyComponent(IComponentProxy component)
    {
        var comment = component.GetString("/comment");

        component.SetAnnotation(
            ComponentStatus.Valid,
            comment,
            comment);
    }
}
