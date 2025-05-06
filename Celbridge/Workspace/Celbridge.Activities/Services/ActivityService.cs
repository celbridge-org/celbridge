using Celbridge.Entities;
using Celbridge.Workspace;

namespace Celbridge.Activities.Services;

public class ActivityService : IActivityService, IDisposable
{
    private IServiceProvider _serviceProvider;
    private IWorkspaceWrapper _workspaceWrapper;

    private ActivityRegistry? _activityRegistry;
    private ActivityDispatcher? _activityDispatcher;

    public ActivityService(
        IServiceProvider serviceProvider,
        IWorkspaceWrapper workspaceWrapper)
    {
        // Only the workspace service is allowed to instantiate this service
        Guard.IsFalse(workspaceWrapper.IsWorkspacePageLoaded);

        _serviceProvider = serviceProvider;
        _workspaceWrapper = workspaceWrapper;
    }

    public async Task<Result> Initialize()
    {
        _activityRegistry = _serviceProvider.GetRequiredService<ActivityRegistry>();
        _activityDispatcher = _serviceProvider.GetRequiredService<ActivityDispatcher>();

        var registryResult = await _activityRegistry.Initialize();
        if (registryResult.IsFailure)
        {
            return Result.Fail("Failed to initialize the activity registry")
                .WithErrors(registryResult);
        }

        var dispatcherResult = await _activityDispatcher.Initialize(_activityRegistry);
        if (dispatcherResult.IsFailure)
        {
            return Result.Fail("Failed to initialize the activity dispatcher")
                .WithErrors(dispatcherResult);
        }

        return Result.Ok();
    }

    public Result<IActivity> GetActivity(string activityName)
    {
        if (_activityRegistry is null)
        {
            return Result<IActivity>.Fail("Activity registry is not initialized");
        }

        if (_activityRegistry.Activities.TryGetValue(activityName, out var activity))
        {
            return Result<IActivity>.Ok(activity);
        }

        return Result<IActivity>.Fail($"Activity not found: '{activityName}'");
    }

    public async Task<Result> UpdateAsync()
    {
        if (_activityDispatcher is null)
        {
            // noop
            return Result.Ok();
        }

        return await _activityDispatcher.UpdateAsync();
    }

    public Result<IEntityAnnotation> AnnotateEntity(ResourceKey fileResource)
    {
        Guard.IsNotNull(_activityRegistry);

        var entityService = _workspaceWrapper.WorkspaceService.EntityService;

        // Get the component list for this entity
        var getComponentsResult = entityService.GetComponents(fileResource);
        if (getComponentsResult.IsFailure)
        {
            return Result<IEntityAnnotation>.Fail($"Failed to get entity components for resource: '{fileResource}'")
                .WithErrors(getComponentsResult);
        }
        var components = getComponentsResult.Value;

        // Create a new entity annotation
        var entityAnnotation = _serviceProvider.GetRequiredService<IEntityAnnotation>();
        entityAnnotation.Initialize(components.Count);

        if (components.Count == 0)
        {
            // Entity has no components, early out.
            return Result<IEntityAnnotation>.Ok(entityAnnotation);
        }

        bool hasValidRootComponent = true;

        // Get the root component and check that it has a "rootActivity" attribute
        var rootComponent = components[0];
        var activityName = rootComponent.Schema.GetStringAttribute("rootActivity");
        if (string.IsNullOrEmpty(activityName))
        {
            hasValidRootComponent = false;

            entityAnnotation.AddComponentError(0, new AnnotationError(
                AnnotationErrorSeverity.Critical,
                "Invalid root component",
                "This component is not a valid root component for this resource."));
        }

        // Perform some basic configuation checks for all components
        for (int i = 0; i < components.Count; i++)
        {
            // Empty components are always valid in any position.
            var component = components[i];
            if (component.Schema.ComponentType == EntityConstants.EmptyComponentType)
            {
                entityAnnotation.SetIsRecognized(i);
            }
            else if (i > 0)
            {
                // Check if this is a root component that's in the wrong position
                var rootActivity = component.Schema.GetStringAttribute("rootActivity");
                if (!string.IsNullOrEmpty(rootActivity))
                {
                    hasValidRootComponent = false;

                    entityAnnotation.AddComponentError(i, new AnnotationError(
                        AnnotationErrorSeverity.Critical,
                        "Invalid position",
                        "The root component must be the first component in the list."));
                }
            }
        }

        if (!hasValidRootComponent ||
            activityName == "None")
        {
            // The Empty root component uses the "None" activity to indicate that no activity should be
            // applied to the entity.

            // Flag any non-empty component as an error.
            for (int i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (component.Schema.ComponentType != EntityConstants.EmptyComponentType)
                {
                    entityAnnotation.AddComponentError(i, new AnnotationError(
                        AnnotationErrorSeverity.Critical,
                        "Invalid component",
                        "This component is not valid because the root component for this resource is not valid."));
                }
            }
        }

        // Lookup the activity by name
        IActivity? activity = null;
        if (hasValidRootComponent &&
            !_activityRegistry.Activities.TryGetValue(activityName, out activity))
        {
            // An activity name was specified, but the activity doesn't actually exist
            hasValidRootComponent = false;
        }

        if (hasValidRootComponent)
        {
            entityAnnotation.ActivityName = activityName;
        }
        else
        {
            entityAnnotation.AddEntityError(new AnnotationError(
                AnnotationErrorSeverity.Error,
                "Invalid root component",
                "The root component is not valid for this resource."));

            // We can't do any more annotating of this entity because it does not specify a valid activity.
            return Result<IEntityAnnotation>.Ok(entityAnnotation);
        }

        // Use the activity to finish annotating the entity
        Guard.IsNotNull(activity);
        var updateAnnotationResult = activity.AnnotateEntity(fileResource, entityAnnotation);
        if (updateAnnotationResult.IsFailure)
        {
            return Result<IEntityAnnotation>.Fail($"Failed to update entity annotation for resource '{fileResource}'")
                .WithErrors(updateAnnotationResult);
        }

        return Result<IEntityAnnotation>.Ok(entityAnnotation);
    }

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed objects here

                _activityRegistry?.Uninitialize();
                _activityDispatcher?.Uninitialize();
            }

            _disposed = true;
        }
    }

    ~ActivityService()
    {
        Dispose(false);
    }
}
