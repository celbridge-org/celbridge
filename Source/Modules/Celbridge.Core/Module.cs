using Celbridge.Activities;
using Celbridge.Core.Components;
using Celbridge.Documents;
using Celbridge.Modules;
using Celbridge.Packages;

namespace Celbridge.Core;

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

    public IReadOnlyList<IDocumentEditorFactory> CreateDocumentEditorFactories(IServiceProvider serviceProvider)
    {
        return [];
    }

    public Result<IActivity> CreateActivity(string activityName)
    {
        return Result<IActivity>.Fail();
    }

    public IReadOnlyList<BundledPackageDescriptor> GetBundledPackages()
    {
        return Array.Empty<BundledPackageDescriptor>();
    }
}
