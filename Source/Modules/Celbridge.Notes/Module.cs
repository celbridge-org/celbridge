using Celbridge.Activities;
using Celbridge.Modules;

namespace Celbridge.Notes;

public class Module : IModule
{
    public IReadOnlyList<string> SupportedActivities { get; } = new List<string>();

    public void ConfigureServices(IModuleServiceCollection services)
    {
    }

    public Result Initialize()
    {
        return Result.Ok();
    }

    public IReadOnlyList<IDocumentEditorFactory> CreateDocumentEditorFactories(IServiceProvider serviceProvider)
    {
        return [];
    }

    public Result<IActivity> CreateActivity(string activityName)
    {
        return Result<IActivity>.Fail();
    }

    public string? GetBundledPackageFolder()
    {
        return Path.Combine(AppContext.BaseDirectory, "Celbridge.Notes", "Web", "note");
    }
}
