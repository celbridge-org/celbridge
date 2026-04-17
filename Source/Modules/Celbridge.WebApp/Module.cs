using Celbridge.Activities;
using Celbridge.Documents;
using Celbridge.Modules;
using Microsoft.Extensions.Localization;
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

    public IReadOnlyList<IDocumentEditorFactory> CreateDocumentEditorFactories(IServiceProvider serviceProvider)
    {
        var stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();
        return [new WebAppEditorFactory(serviceProvider, stringLocalizer)];
    }

    public Result<IActivity> CreateActivity(string activityName)
    {
        return Result<IActivity>.Fail();
    }

    public string? GetBundledPackageFolder()
    {
        return null;
    }
}
