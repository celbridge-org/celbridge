using Celbridge.Entities;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Modules;
using Celbridge.Projects;
using Celbridge.Workspace;

namespace Celbridge.Activities.Services;

public class ActivityRegistry
{
    private readonly ILogger<ActivityRegistry> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IProjectService _projectService;
    private readonly IModuleService _moduleService;
    private readonly IResourceRegistry _resourceRegistry;

    private ResourceKey _projectFileResource;

    private Dictionary<string, IActivity> _activities = new();
    public IReadOnlyDictionary<string, IActivity> Activities => _activities;

    private List<string> _activityNames = new();
    public IReadOnlyList<string> ActivityNames => _activityNames;

    public ActivityRegistry(
        ILogger<ActivityRegistry> logger,
        IMessengerService messengerService,
        IModuleService moduleService,
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _messengerService = messengerService;
        _moduleService = moduleService;
        _projectService = projectService;
        _resourceRegistry = workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;
    }

    public async Task<Result> Initialize()
    {
        // Get the project resource

        var projectPath = _projectService.CurrentProject!.ProjectFilePath;
        var getResourceResult = _resourceRegistry.GetResourceKey(projectPath);
        if (getResourceResult.IsFailure)
        {
            return Result.Fail("Failed to get the project resource key")
                .WithErrors(getResourceResult);
        }
        _projectFileResource = getResourceResult.Value;

        // Create all activities
        // Todo: Ensure that the project file entity has an ActivityComponent for each available activity.

        var names = _moduleService.SupportedActivities.ToList();
        names.Sort(); // Ensure stable creation and initialization order
        _activityNames.AddRange(names);

        foreach (var activityName in ActivityNames)
        {
            var createResult = _moduleService.CreateActivity(activityName);
            if (createResult.IsFailure)
            {
                return Result.Fail($"Failed to create activity: '{activityName}'")
                    .WithErrors(createResult);
            }
            var activity = createResult.Value;

            _activities[activityName] = activity;
        }

        // Activate all activities
        // Todo: Only activate activities that have been marked as enabled in the project file entity.

        foreach (var activityName in ActivityNames)
        {
            var activity = _activities[activityName];
            var activateResult = await activity.ActivateAsync();
            if (activateResult.IsFailure)
            {
                return Result.Fail($"Failed to activate activity: '{activityName}'")
                    .WithErrors(activateResult);
            }
        }

        // Start listening for activity config changes (i.e. changes to the project file entity)

        _messengerService.Register<ResourceKeyChangedMessage>(this, OnResourceKeyChangedMessage);
        _messengerService.Register<ComponentChangedMessage>(this, OnComponentChangedMessage);

        return Result.Ok();
    }

    public async Task<Result> Uninitialize()
    {
        _messengerService.UnregisterAll(this);

        var activityNames = _moduleService.SupportedActivities.ToList();
        activityNames.Sort(); // Ensure stable deactivation order

        // Deactivate all activities

        foreach (var activityName in activityNames)
        {
            var activity = _activities[activityName];
            var deactivateResult = await activity.DeactivateAsync();
            if (deactivateResult.IsFailure)
            {
                return Result.Fail($"Failed to deactivate activity: '{activityName}'")
                    .WithErrors(deactivateResult);
            }
        }

        return Result.Ok();
    }

    public List<IActivity> GetSupportingActivities(ResourceKey resource)
    {
        var activities = new List<IActivity>();
        foreach (var activityName in ActivityNames)
        {
            var activity = _activities[activityName];
            if (activity.SupportsResource(resource))
            {
                activities.Add(activity);                
            }
        }

        return activities;
    }

    private void OnComponentChangedMessage(object recipient, ComponentChangedMessage message)
    {
        var resource = message.ComponentKey.Resource;
        var propertyPath = message.PropertyPath;

        if (resource == _projectFileResource &&
            propertyPath == "/")
        {
            NotifyActivityConfigChanged();
        }
    }

    private void OnResourceKeyChangedMessage(object recipient, ResourceKeyChangedMessage message)
    {
        if (message.SourceResource == _projectFileResource)
        {
            // Project file has been renamed or moved
            _projectFileResource = message.DestResource;

            NotifyActivityConfigChanged();
        }
    }

    private void NotifyActivityConfigChanged()
    {
        // Notify activities that the activity config on the project file entity has been modified
        var message = new ActivityConfigChangedMessage(_projectFileResource);
        _messengerService.Send(message);
    }
}
