using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Activities.Services;

public class ActivityService : IActivityService, IDisposable
{
    private IServiceProvider _serviceProvider;

    private ActivityRegistry? _activityRegistry;
    private ActivityDispatcher? _activityDispatcher;

    public ActivityService(
        IServiceProvider serviceProvider,
        IWorkspaceWrapper workspaceWrapper)
    {
        // Only the workspace service is allowed to instantiate this service
        Guard.IsFalse(workspaceWrapper.IsWorkspacePageLoaded);

        _serviceProvider = serviceProvider;
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
