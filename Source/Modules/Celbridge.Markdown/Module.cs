using Celbridge.Activities;
using Celbridge.Markdown.Services;
using Celbridge.Modules;
using Microsoft.Extensions.Localization;

namespace Celbridge.Markdown;

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
        var stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();
        return [new MarkdownEditorFactory(serviceProvider, stringLocalizer)];
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
