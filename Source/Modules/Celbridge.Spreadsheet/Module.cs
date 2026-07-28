using Celbridge.Activities;
using Celbridge.Modules;
using Celbridge.Packages;
using Celbridge.Screenplay.Components;
using Celbridge.Spreadsheet.Commands;
using Celbridge.Spreadsheet.Services;
using Celbridge.WebHost;

namespace Celbridge.Spreadsheet;

/// <summary>
/// SpreadJS spreadsheet editor integration.
/// Bundles the "celbridge.spreadsheet" package.
/// </summary>
public class Module : IModule
{
    public IReadOnlyList<string> SupportedActivities { get; } = new List<string>()
    {
        nameof(SpreadsheetActivity)
    };

    public void ConfigureServices(IModuleServiceCollection services)
    {
        services.AddTransient<SpreadsheetActivity>();
        services.AddTransient<SpreadsheetEditor>();
        services.AddSingleton<ISpreadsheetReader, SpreadsheetReader>();

        // Hosts the SpreadJS editor under its synthetic origin. Registered after the core loopback default,
        // so the custom view resolves it ahead of the default for the spreadsheet package.
        services.AddSingleton<ICustomEditorLoader, SyntheticOriginEditorLoader>();

        services.AddSingleton<IBundledPackageProvider, SpreadsheetBundledPackageProvider>();

        services.AddTransient<IWriteCellsCommand, WriteCellsCommand>();
        services.AddTransient<IAppendRowsCommand, AppendRowsCommand>();
        services.AddTransient<IImportCsvCommand, ImportCsvCommand>();
        services.AddTransient<IAddSheetsCommand, AddSheetsCommand>();
        services.AddTransient<IRemoveSheetCommand, RemoveSheetCommand>();
        services.AddTransient<IRenameSheetCommand, RenameSheetCommand>();
        services.AddTransient<IMoveSheetCommand, MoveSheetCommand>();
        services.AddTransient<ISetActiveViewCommand, SetActiveViewCommand>();
        services.AddTransient<IFormatRangesCommand, FormatRangesCommand>();
        services.AddTransient<IFreezePanesCommand, FreezePanesCommand>();
        services.AddTransient<IDeleteRangesCommand, DeleteRangesCommand>();
        services.AddTransient<IClearRangesCommand, ClearRangesCommand>();
        services.AddTransient<IInsertRangesCommand, InsertRangesCommand>();
        services.AddTransient<ISortRangeCommand, SortRangeCommand>();
        services.AddTransient<IDuplicateSheetCommand, DuplicateSheetCommand>();
        services.AddTransient<ISetAutoFilterCommand, SetAutoFilterCommand>();
        services.AddTransient<ISetConditionalFormattingCommand, SetConditionalFormattingCommand>();
    }

    public Result Initialize()
    {
        return Result.Ok();
    }

    public IReadOnlyList<IDocumentEditorFactory> CreateDocumentEditorFactories(IServiceProvider serviceProvider)
    {
        return Array.Empty<IDocumentEditorFactory>();
    }

    public Result<IActivity> CreateActivity(string activityName)
    {
        if (activityName == nameof(SpreadsheetActivity))
        {
            var activity = ServiceLocator.AcquireService<SpreadsheetActivity>();
            return activity;
        }

        return Result.Fail();
    }
}
