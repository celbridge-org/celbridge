using Celbridge.Activities;
using Celbridge.Core.Components;
using Celbridge.Modules;

namespace Celbridge.Workspace;

public class Module : IModule
{
    public IReadOnlyList<string> SupportedActivities { get; } = new List<string>() {};

    public void ConfigureServices(IModuleServiceCollection services)
    {
        //
        // Register component editors
        //

        services.AddTransient<EmptyEditor>();
    }

    public Result Initialize()
    {
        return Result.Ok();
    }

    public Result<IActivity> CreateActivity(string activityName)
    {
        return Result<IActivity>.Fail();
    }
}
