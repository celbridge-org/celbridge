using Celbridge.Modules;
using Celbridge.Packages;
using Celbridge.Spreadsheet.Commands;
using Celbridge.Spreadsheet.Services;
using Celbridge.WebHost;

namespace Celbridge.Spreadsheet;

/// <summary>
/// SpreadJS spreadsheet editor integration.
/// Bundles the "celbridge-spreadsheet" package.
/// </summary>
public class Module : IModule
{
    public void ConfigureServices(IModuleServiceCollection services)
    {
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
}
