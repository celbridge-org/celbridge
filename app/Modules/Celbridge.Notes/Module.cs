using Celbridge.Activities;
using Celbridge.Modules;
using Celbridge.Notes.ComponentEditors;
using Celbridge.Notes.Services;

namespace Celbridge.Notes;

public class Module : IModule
{
    public IReadOnlyList<string> SupportedActivities { get; } = new List<string>()
    {
        nameof(NoteActivity)
    };

    public void ConfigureServices(IModuleServiceCollection services)
    {
        //
        // Register services
        //

        services.AddTransient<NoteActivity>();

        //
        // Register component editors
        //

        services.AddTransient<NoteEditor>();
    }

    public Result Initialize()
    {
        return Result.Ok();
    }

    public Result<IActivity> CreateActivity(string activityName)
    {
        if (activityName == nameof(NoteActivity))
        {
            var activity = ServiceLocator.AcquireService<NoteActivity>();
            return Result<IActivity>.Ok(activity);
        }

        return Result<IActivity>.Fail();
    }
}
