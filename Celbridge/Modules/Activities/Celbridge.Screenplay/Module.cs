using Celbridge.Activities;
using Celbridge.Modules;
using Celbridge.Screenplay.Services;

namespace Celbridge.Workspace;

public class Module : IModule
{
    public void ConfigureServices(IModuleServiceCollection config)
    {
        //
        // Register services
        //

        config.AddTransient<ScreenplayActivity>();
    }

    public Result Initialize()
    {
        return Result.Ok();
    }

    public bool SupportsActivity(string activityName) => activityName == ScreenplayConstants.ScreenplayActivityName;

    public Result<IActivity> CreateActivity(string activityName)
    {
        var serviceProvider = ServiceLocator.ServiceProvider;

        if (activityName == ScreenplayConstants.ScreenplayActivityName)
        {
            var activity = serviceProvider.GetRequiredService<ScreenplayActivity>();
            return Result<IActivity>.Ok(activity);
        }

        return Result<IActivity>.Fail();
    }
}
