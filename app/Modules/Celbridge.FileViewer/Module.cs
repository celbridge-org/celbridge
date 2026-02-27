using Celbridge.Activities;
using Celbridge.Documents;
using Celbridge.FileViewer.Services;
using Celbridge.FileViewer.ViewModels;
using Celbridge.FileViewer.Views;
using Celbridge.Modules;

namespace Celbridge.FileViewer;

public class Module : IModule
{
    public IReadOnlyList<string> SupportedActivities { get; } = new List<string>();

    public void ConfigureServices(IModuleServiceCollection services)
    {
        //
        // Register document editor factories
        //

        services.AddTransient<IDocumentEditorFactory, FileViewerFactory>();

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

    public Result<IActivity> CreateActivity(string activityName)
    {
        return Result<IActivity>.Fail();
    }
}
