using Celbridge.Activities;
using Celbridge.Modules;
using Celbridge.Notes.Services;
using Celbridge.Notes.ViewModels;
using Celbridge.Notes.Views;

namespace Celbridge.Notes;

public class Module : IModule
{
    public IReadOnlyList<string> SupportedActivities { get; } = new List<string>();

    public void ConfigureServices(IModuleServiceCollection services)
    {
        //
        // Register document editor factories
        //

        services.AddTransient<IDocumentEditorFactory, NoteEditorFactory>();

        //
        // Register views
        //

        services.AddTransient<NoteDocumentView>();

        //
        // Register view models
        //

        services.AddTransient<NoteDocumentViewModel>();
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
