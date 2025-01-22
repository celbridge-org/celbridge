using Celbridge.Activities;
using Celbridge.Modules;
using Celbridge.Screenplay.Components;
using Celbridge.Screenplay.Services;

namespace Celbridge.Workspace;

public class Module : IModule
{
    public IReadOnlyList<string> SupportedActivities { get; } = new List<string>()
    {
        ScreenplayActivityEditor.ActivityName
    };

    public void ConfigureServices(IModuleServiceCollection config)
    {
        //
        // Register services
        //

        config.AddTransient<ScreenplayActivity>();

        //
        // Register compoments
        //

        config.AddTransient<LineEditor>();
        config.AddTransient<SceneEditor>();
        config.AddTransient<ScreenplayActivityEditor>();

        // Todo: Move these to more appropriate modules
        config.AddTransient<EmptyEditor>();
        config.AddTransient<MarkdownEditor>();
    }

    public Result Initialize()
    {
        return Result.Ok();
    }

    public Result<IActivity> CreateActivity(string activityName)
    {
        var serviceProvider = ServiceLocator.ServiceProvider;

        if (activityName == ScreenplayActivityEditor.ActivityName)
        {
            var activity = serviceProvider.GetRequiredService<ScreenplayActivity>();
            return Result<IActivity>.Ok(activity);
        }

        return Result<IActivity>.Fail();
    }
}
