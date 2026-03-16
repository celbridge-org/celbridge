using Celbridge.Activities;
using Celbridge.Code.Services;
using Celbridge.Code.ViewModels;
using Celbridge.Code.Views;
using Celbridge.Modules;

namespace Celbridge.Code;

public class Module : IModule
{
    public IReadOnlyList<string> SupportedActivities { get; } = new List<string>();

    public void ConfigureServices(IModuleServiceCollection services)
    {
        //
        // Register views
        //

        services.AddTransient<CodeEditorDocumentView>();

        //
        // Register view models
        //

        services.AddTransient<CodeEditorViewModel>();
    }

    public Result Initialize()
    {
        return Result.Ok();
    }

    public IReadOnlyList<IDocumentEditorFactory> CreateDocumentEditorFactories(IServiceProvider serviceProvider)
    {
        return [new CodeEditorFactory(serviceProvider)];
    }

    public Result<IActivity> CreateActivity(string activityName)
    {
        return Result<IActivity>.Fail();
    }

    public string? GetExtensionFolder()
    {
        return null;
    }
}
