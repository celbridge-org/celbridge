using Celbridge.Activities;
using Celbridge.Documents;
using Celbridge.Documents.Services;
using Celbridge.FileViewer.Services;
using Celbridge.FileViewer.ViewModels;
using Celbridge.FileViewer.Views;
using Celbridge.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.FileViewer;

public class Module : IModule
{
    public IReadOnlyList<string> SupportedActivities { get; } = new List<string>();

    public void ConfigureServices(IModuleServiceCollection services)
    {
        //
        // Register views
        //

        services.AddTransient<FileViewerDocumentView>();

        //
        // Register view models
        //

        services.AddTransient<FileViewerDocumentViewModel>();
    }

    public Result Initialize()
    {
        return Result.Ok();
    }

    public IReadOnlyList<IDocumentEditorFactory> CreateDocumentEditorFactories(IServiceProvider serviceProvider)
    {
        var fileTypeHelper = serviceProvider.GetRequiredService<FileTypeHelper>();
        return [new FileViewerFactory(serviceProvider, fileTypeHelper)];
    }

    public Result<IActivity> CreateActivity(string activityName)
    {
        return Result<IActivity>.Fail();
    }

    public string? GetBundledExtensionFolder()
    {
        return null;
    }
}
