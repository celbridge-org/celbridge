using Celbridge.Activities;
using Celbridge.Modules;

namespace Celbridge.Notes;

public class Module : IModule
{
    public IReadOnlyList<string> SupportedActivities { get; } = new List<string>();

    public void ConfigureServices(IModuleServiceCollection services)
    {
        //
        // Register bundled extension provider
        //

        services.AddSingleton<IBundledExtensionProvider, NoteBundledExtensionProvider>();
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
