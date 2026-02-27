using Celbridge.Activities;
using Celbridge.Documents;
using Celbridge.Modules;
using Celbridge.Screenplay.Components;
using Celbridge.Spreadsheet.Services;
using Celbridge.Spreadsheet.ViewModels;
using Celbridge.Spreadsheet.Views;

namespace Celbridge.Spreadsheet;

public class Module : IModule
{
    public IReadOnlyList<string> SupportedActivities { get; } = new List<string>()
    {
        nameof(SpreadsheetActivity)
    };

    public void ConfigureServices(IModuleServiceCollection services)
    {
        //
        // Register services
        //

        services.AddTransient<SpreadsheetActivity>();

        //
        // Register document editor factories
        //

        services.AddTransient<IDocumentEditorFactory, SpreadsheetEditorFactory>();

        //
        // Register views
        //

        services.AddTransient<SpreadsheetDocumentView>();

        //
        // Register view models
        //

        services.AddTransient<SpreadsheetDocumentViewModel>();

        //
        // Register components
        //

        services.AddTransient<SpreadsheetEditor>();
    }

    public Result Initialize()
    {
        return Result.Ok();
    }

    public Result<IActivity> CreateActivity(string activityName)
    {
        if (activityName == nameof(SpreadsheetActivity))
        {
            var activity = ServiceLocator.AcquireService<SpreadsheetActivity>();
            return Result<IActivity>.Ok(activity);
        }

        return Result<IActivity>.Fail();
    }
}
