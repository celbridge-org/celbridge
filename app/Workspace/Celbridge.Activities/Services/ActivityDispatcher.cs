using Celbridge.Entities;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Workspace;

namespace Celbridge.Activities.Services;

public class ActivityDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ActivityDispatcher> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IEntityService _entityService;
    private readonly IActivityService _activityService;

    private ActivityRegistry? _activityRegistry;

    private List<ResourceKey> _pendingInits = new();
    private List<ResourceKey> _pendingUpdates = new();

    public ActivityDispatcher(
        IServiceProvider serviceProvider,
        ILogger<ActivityDispatcher> logger,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        _messengerService = messengerService;
        _entityService = workspaceWrapper.WorkspaceService.EntityService;
        _activityService = workspaceWrapper.WorkspaceService.ActivityService;
    }

    public async Task<Result> Initialize(ActivityRegistry activityRegistry)
    {
        _activityRegistry = activityRegistry;

        _messengerService.Register<EntityCreatedMessage>(this, (s, e) => OnInitMessage(e.Resource));

        void OnInitMessage(ResourceKey resource)
        {
            if (!resource.IsEmpty)
            {
                // Make a note of the new entity for initialization
                _pendingInits.AddDistinct(resource);
            }
        }

        _messengerService.Register<SelectedResourceChangedMessage>(this, (s, e) => OnUpdateMessage(e.Resource));
        _messengerService.Register<ComponentChangedMessage>(this, (s, e) => OnUpdateMessage(e.ComponentKey.Resource));

        void OnUpdateMessage(ResourceKey resource)
        {
            if (!resource.IsEmpty)
            {
                // Make a note of the entity for updating
                _pendingUpdates.AddDistinct(resource);
            }
        }

        await Task.CompletedTask;

        return Result.Ok();
    }

    public void Uninitialize()
    {
        _messengerService.UnregisterAll(this);
    }

    public async Task<Result> UpdateAsync()
    {
        Guard.IsNotNull(_activityRegistry);

        var initsResult = await PerformPendingInits();
        var updatesResult = await PerformPendingUpdates();

        if (initsResult.IsFailure)
        {
            return initsResult;
        }

        if (updatesResult.IsFailure)
        {
            return updatesResult;
        }

        return Result.Ok();
    }

    private async Task<Result> PerformPendingInits()
    {
        // Perform pending inits
        var pendingInits = _pendingInits.ToList();
        _pendingInits.Clear();

        bool failed = false;
        foreach (var fileResource in pendingInits)
        {
            try
            {
                var initResult = await InitializeResource(fileResource);
                if (initResult.IsFailure)
                {
                    failed = true;
                    _logger.LogError(initResult.Error);
                    continue;
                }
            }
            catch (Exception ex)
            {
                failed = true;
                _logger.LogError(ex.ToString());
            }
        }

        if (failed)
        {
            return Result.Fail($"Failed to perform pending inits");
        }

        return Result.Ok();
    }

    private async Task<Result> PerformPendingUpdates()
    {
        Guard.IsNotNull(_activityRegistry);

        bool failed = false;

        var pendingUpdates = _pendingUpdates.ToList();
        _pendingUpdates.Clear();

        var entityAnnotations = new Dictionary<string, IEntityAnnotation>();

        foreach (var fileResource in pendingUpdates)
        {
            try
            {
                var annotateResult = _activityService.AnnotateEntity(fileResource);
                if (annotateResult.IsFailure)
                {
                    _logger.LogError(annotateResult.Error);
                    continue;
                }
                var entityAnnotation = annotateResult.Value;

                if (_activityRegistry.Activities.TryGetValue(entityAnnotation.ActivityName, out var activity))
                {
                    // Give the activity an opportunity to update the resource content.
                    var updateResourceResult = await activity.UpdateResourceContentAsync(fileResource, entityAnnotation);
                    if (updateResourceResult.IsFailure)
                    {
                        return Result<IEntityAnnotation>.Fail($"Failed to update entity resource '{fileResource}'")
                            .WithErrors(updateResourceResult);
                    }
                }

                entityAnnotations[fileResource] = entityAnnotation;
            }
            catch (Exception ex)
            {
                failed = true;
                _logger.LogError(ex.ToString());
            }
        }

        // Send a message to the inspector to apply the annotations
        foreach (var kv in entityAnnotations)
        {
            var entity = kv.Key;
            var entityAnnotation = kv.Value;

            var message = new AnnotatedEntityMessage(entity, entityAnnotation);
            _messengerService.Send(message);
        }

        if (failed)
        {
            return Result.Fail($"Failed to perform pending updates");
        }

        return Result.Ok();
    }

    private async Task<Result> InitializeResource(ResourceKey resource)
    {
        Guard.IsNotNull(_activityRegistry);

        // Search for an activity that supports this resource.
        // Try each activity in alphabetic order for deterministic results.

        var activityNames = _activityRegistry.ActivityNames;
        foreach (var activityName in activityNames)
        {
            var activity = _activityRegistry.Activities[activityName];

            if (activity.SupportsResource(resource))
            {
                // Initialize the resource with this activity
                var initializeResult = await activity.InitializeResourceAsync(resource);
                if (initializeResult.IsFailure)
                {
                    return Result.Fail($"Failed to initialize resource '{resource}' with activity '{activityName}'");
                }
                break;
            }
        }

        // If no activity supports the resource then no initialization is necessary

        return Result.Ok();
    }
}
