using Celbridge.Activities;
using Celbridge.Documents;
using Celbridge.Modules;
using Celbridge.WebApp.Services;
using Celbridge.WebApp.ViewModels;
using Celbridge.WebApp.Views;

namespace Celbridge.WebApp;

public class Module : IModule
{
    public IReadOnlyList<string> SupportedActivities { get; } = new List<string>();

    public void ConfigureServices(IModuleServiceCollection services)
    {
        //
        // Register document editor factories
        //

        services.AddTransient<IDocumentEditorFactory, WebAppEditorFactory>();

        //
        // Register views
        //

        services.AddTransient<WebAppDocumentView>();

        //
        // Register view models
        //

        services.AddTransient<WebAppDocumentViewModel>();
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
