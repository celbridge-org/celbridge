using Celbridge.Entities;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Modules;
using Celbridge.Projects;
using Celbridge.Workspace;

namespace Celbridge.Activities.Services;

public class ActivityService : IActivityService, IDisposable
{
    private ILogger<ActivityService> _logger;
    private IMessengerService _messengerService;
    private IProjectService _projectService;
    private IModuleService _moduleService;
    private IWorkspaceWrapper _workspaceWrapper;

    private ResourceKey _projectFileResource;

    private Dictionary<string, IActivity> _activities = new();

    public ActivityService(
        ILogger<ActivityService> logger,
        IMessengerService messengerService,
        IProjectService projectService,
        IModuleService moduleService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _messengerService = messengerService;
        _projectService = projectService;
        _moduleService = moduleService;
        _workspaceWrapper = workspaceWrapper;

        _messengerService.Register<ResourceKeyChangedMessage>(this, OnResourceKeyChangedMessage);
        _messengerService.Register<ComponentChangedMessage>(this, OnComponentChangedMessage);
    }

    private void OnComponentChangedMessage(object recipient, ComponentChangedMessage message)
    {
        var resource = message.Resource;
        var propertyPath = message.PropertyPath;

        if (resource == _projectFileResource &&
            propertyPath == "/")
        {
            _ = PopulateRunningActivities();
        }
    }

    public async Task<Result> Initialize()
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;

        // Get the project resource

        var projectPath = _projectService.CurrentProject!.ProjectFilePath;
        var getResourceResult = resourceRegistry.GetResourceKey(projectPath);
        if (getResourceResult.IsFailure)
        {
            return Result.Fail("Failed to get the project resource key")
                .WithErrors(getResourceResult);
        }
        _projectFileResource = getResourceResult.Value;

        // Populate the list of supported activities

        var populateResult = await PopulateRunningActivities();
        if (populateResult.IsFailure)
        {
            return Result.Fail("Failed to populate activity list")
                .WithErrors(populateResult);
        }

        return Result.Ok();
    }

    public async Task<Result> UpdateActivitiesAsync()
    {
        try
        {
            foreach (var kv in _activities)
            {
                var activity = kv.Value;

                var updateResult = await activity.UpdateAsync();
                if (updateResult.IsFailure)
                {
                    return Result.Fail($"Failed to update activity '{activity.GetType()}'")
                        .WithErrors(updateResult);
                }
            }
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred while updating activities")
                .WithException(ex);
        }

        return Result.Ok();
    }

    private void OnResourceKeyChangedMessage(object recipient, ResourceKeyChangedMessage message)
    {
        if (message.SourceResource == _projectFileResource)
        {
            // Project file has been renamed or moved
            _projectFileResource = message.DestResource;

            _ = PopulateRunningActivities();
        }
    }

    private async Task<Result> PopulateRunningActivities()
    {
        var entityService = _workspaceWrapper.WorkspaceService.EntityService;

        // Determine the set of required activities based on the project file entity components
        var getCountResult = entityService.GetComponentCount(_projectFileResource);
        if (getCountResult.IsFailure)
        {
            return Result.Fail($"Failed to get component count for project file resource: '{_projectFileResource}'")
                .WithErrors(getCountResult);
        }
        var componentCount = getCountResult.Value;

        var requiredActivityNames = new HashSet<string>();

        for (int i = 0; i < componentCount; i++)
        {
            var getInfoResult = entityService.GetComponentTypeInfo(_projectFileResource, i);
            if (getInfoResult.IsFailure)
            {
                return Result.Fail($"Failed to get component info for component index '{i}' on project file resource: '{_projectFileResource}'")
                    .WithErrors(getInfoResult);
            }
            var componentInfo = getInfoResult.Value;

            bool isActivity = componentInfo.GetBooleanAttribute("isActivityComponent");
            if (!isActivity)
            {
                // Ignore non-activity components
                continue; 
            }

            // Add this activity name to the list of required activities

            var activityName = componentInfo.ComponentType.Replace("Activity", string.Empty);
            requiredActivityNames.Add(activityName);
        }

        // Stop and remove activities that are no longer required.
        var runningActivityNames = _activities.Keys.ToList(); 
        foreach (var activityName in runningActivityNames)
        {
            if (!requiredActivityNames.Contains(activityName))
            {
                if (_activities.TryGetValue(activityName, out var activity))
                {
                    var stopResult = await activity.Stop();
                    if (stopResult.IsFailure)
                    {
                        return Result.Fail($"Failed to stop activity: '{activityName}'")
                            .WithErrors(stopResult);
                    }
                    _activities.Remove(activityName);
                }
            }
        }

        // Todo: We probably shouldn't allow any activities to run until the project components are all configured correctly.

        // Start new required activities
        foreach (var activityName in requiredActivityNames)
        {
            if (!_activities.ContainsKey(activityName))
            {
                if (!_moduleService.IsActivitySupported(activityName))
                {
                    _logger.LogError($"Activity '{activityName}' is not supported by any loaded module.");
                    continue;
                }

                var createActivityResult = _moduleService.CreateActivity(activityName);
                if (createActivityResult.IsFailure)
                {
                    _logger.LogError($"Failed to create activity '{activityName}'.");
                    continue;
                }

                var createdActivity = createActivityResult.Value;
                _activities[activityName] = createdActivity;

                var startResult = await createdActivity.Start();
                if (startResult.IsFailure)
                {
                    return Result.Fail($"Failed to start activity '{activityName}'")
                        .WithErrors(startResult);
                }
            }
        }

        return Result.Ok();
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
            }

            _disposed = true;
        }
    }

    ~ActivityService()
    {
        Dispose(false);
    }
}
