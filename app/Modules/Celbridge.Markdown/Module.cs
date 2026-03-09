using Celbridge.Activities;
using Celbridge.Markdown.Services;
using Celbridge.Markdown.ViewModels;
using Celbridge.Markdown.Views;
using Celbridge.Modules;

namespace Celbridge.Markdown;

public class Module : IModule
{
    public IReadOnlyList<string> SupportedActivities { get; } = new List<string>();

    public void ConfigureServices(IModuleServiceCollection services)
    {
        //
        // Register document editor factories
        //

        services.AddTransient<IDocumentEditorFactory, MarkdownEditorFactory>();

        //
        // Register views
        //

        services.AddTransient<MarkdownDocumentView>();

        //
        // Register view models
        //

        services.AddTransient<MarkdownDocumentViewModel>();
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
