using Celbridge.Activities;
using Celbridge.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Modules.Services;

public class ModuleService : IModuleService
{
    private ILogger<ModuleService> _logger;

    private static ModuleLoader _moduleLoader = new();

    public ModuleService(ILogger<ModuleService> logger)
    {
        _logger = logger;
    }

    public Result InitializeModules()
    {
        var initResult = _moduleLoader.InitializeModules();
        if (initResult.IsFailure)
        {
            return Result.Fail("Failed to initialize modules");
        }

        foreach (var module in _moduleLoader.LoadedModules.Keys)
        {
            _logger.LogDebug("Initialized module: {0}", module);
        }

        return Result.Ok();
    }

    public static Result LoadModules(List<string> modules, IServiceCollection services)
    {
        // Load Modules
        foreach (var module in modules)
        {
            var loadResult = _moduleLoader.LoadModules(module);
            if (loadResult.IsFailure)
            {
                return Result.Fail($"Failed to load module {module}")
                    .WithErrors(loadResult);
            }
        }

        var loadedModules = _moduleLoader.LoadedModules.Values.ToList();

        // Register the services provided by each module with the dependency injection framework.
        var moduleServices = new ModuleServiceCollection();
        foreach (var module in loadedModules)
        {
            module.ConfigureServices(moduleServices);
        }
        moduleServices.PopulateServices(services);

        return Result.Ok();
    }

    public IReadOnlyList<string> SupportedActivities
    {
        get
        {
            // Sort the keys to ensure stable activity name order
            var sortedKeys = _moduleLoader.LoadedModules.Keys.ToList();
            sortedKeys.Sort();

            var supportedActivities = new List<string>();
            foreach (var key in sortedKeys)
            {
                var module = _moduleLoader.LoadedModules[key];
                supportedActivities.AddRange(module.SupportedActivities);
            }
            return supportedActivities;
        }
    }

    public Result<IActivity> CreateActivity(string activityName)
    {
        foreach (var module in _moduleLoader.LoadedModules.Values)
        {
            if (!module.SupportedActivities.Contains(activityName))
            {
                continue;
            }

            var createResult = module.CreateActivity(activityName);
            if (createResult.IsFailure)
            {
                return Result<IActivity>.Fail($"Failed to create activity: '{activityName}'");
            }
            var activity = createResult.Value;

            return Result<IActivity>.Ok(activity);
        }

        return Result<IActivity>.Fail($"Failed to create activity: '{activityName}'");
    }
}
