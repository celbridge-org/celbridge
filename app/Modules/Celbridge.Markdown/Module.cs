using Celbridge.Activities;
using Celbridge.Documents;
using Celbridge.Markdown.ComponentEditors;
using Celbridge.Markdown.Services;
using Celbridge.Markdown.ViewModels;
using Celbridge.Markdown.Views;
using Celbridge.Modules;

namespace Celbridge.Markdown;

public class Module : IModule
{
    public IReadOnlyList<string> SupportedActivities { get; } = new List<string>()
    {
        nameof(MarkdownActivity)
    };

    public void ConfigureServices(IModuleServiceCollection services)
    {
        //
        // Register services
        //

        services.AddTransient<MarkdownActivity>();

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

        //
        // Register component editors
        //

        services.AddTransient<MarkdownEditor>();
    }

    public Result Initialize()
    {
        return Result.Ok();
    }

    public Result<IActivity> CreateActivity(string activityName)
    {
        if (activityName == nameof(MarkdownActivity))
        {
            var activity = ServiceLocator.AcquireService<MarkdownActivity>();
            return Result<IActivity>.Ok(activity);
        }

        return Result<IActivity>.Fail();
    }
}
