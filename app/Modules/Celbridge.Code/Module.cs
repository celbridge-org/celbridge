using Celbridge.Activities;
using Celbridge.Code.Services;
using Celbridge.Code.ViewModels;
using Celbridge.Code.Views;
using Celbridge.Documents;
using Celbridge.Modules;

namespace Celbridge.Code;

public class Module : IModule
{
    public IReadOnlyList<string> SupportedActivities { get; } = new List<string>();

    public void ConfigureServices(IModuleServiceCollection services)
    {
        //
        // Register document editor factories
        //

        services.AddTransient<IDocumentEditorFactory, CodeEditorFactory>();

        //
        // Register views
        //

        services.AddTransient<TextEditorDocumentView>();
        services.AddTransient<MonacoEditorView>();

        //
        // Register view models
        //

        services.AddTransient<TextEditorDocumentViewModel>();
        services.AddTransient<MonacoEditorViewModel>();
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
