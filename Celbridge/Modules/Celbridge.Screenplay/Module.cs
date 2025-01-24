using Celbridge.Activities;
using Celbridge.Modules;
using Celbridge.Screenplay.Components;
using Celbridge.Screenplay.Services;

namespace Celbridge.Workspace;

public class Module : IModule
{
    public IReadOnlyList<string> SupportedActivities { get; } = new List<string>()
    {
        nameof(ScreenplayActivity)
    };

    public void ConfigureServices(IModuleServiceCollection services)
    {
        //
        // Register services
        //

        services.AddTransient<ScreenplayActivity>();

        //
        // Register compoments
        //

        services.AddTransient<LineEditor>();
        services.AddTransient<SceneEditor>();
        services.AddTransient<ScreenplayActivityEditor>();
    }

    public Result Initialize()
    {
        return Result.Ok();
    }

    public Result<IActivity> CreateActivity(string activityName)
    {
        if (activityName == nameof(ScreenplayActivity))
        {
            var serviceProvider = ServiceLocator.ServiceProvider;
            var activity = serviceProvider.AcquireService<ScreenplayActivity>();
            return Result<IActivity>.Ok(activity);
        }

        return Result<IActivity>.Fail();
    }
}
