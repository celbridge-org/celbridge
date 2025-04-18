using Celbridge.Activities;
using Celbridge.Documents;
using Celbridge.HTML.Components;
using Celbridge.HTML.Services;
using Celbridge.Modules;

namespace Celbridge.HTML;

public class Module : IModule
{
    public IReadOnlyList<string> SupportedActivities { get; } = new List<string>()
    {
        nameof(HTMLActivity)
    };

    public void ConfigureServices(IModuleServiceCollection services)
    {
        //
        // Register services
        //

        services.AddTransient<HTMLActivity>();
        services.AddTransient<IHTMLPreviewProvider, HTMLPreviewProvider>();

        //
        // Register component editors
        //

        services.AddTransient<HTMLEditor>();
    }

    public Result Initialize()
    {
        return Result.Ok();
    }

    public Result<IActivity> CreateActivity(string activityName)
    {
        if (activityName == nameof(HTMLActivity))
        {
            var activity = ServiceLocator.AcquireService<HTMLActivity>();
            return Result<IActivity>.Ok(activity);
        }

        return Result<IActivity>.Fail();
    }
}
