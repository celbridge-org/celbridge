using Celbridge.Activities;
using Celbridge.Code.Services;
using Celbridge.Code.ViewModels;
using Celbridge.Code.Views;
using Celbridge.Modules;
using Microsoft.Extensions.Localization;

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
        var textBinarySniffer = serviceProvider.GetRequiredService<ITextBinarySniffer>();
        var stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();
        return [new CodeEditorFactory(serviceProvider, textBinarySniffer, stringLocalizer)];
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
