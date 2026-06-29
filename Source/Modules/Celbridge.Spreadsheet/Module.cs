using Celbridge.Activities;
using Celbridge.Logging;
using Celbridge.Modules;
using Celbridge.Packages;
using Celbridge.Screenplay.Components;
using Celbridge.Spreadsheet.Commands;
using Celbridge.Spreadsheet.Services;

namespace Celbridge.Spreadsheet;

/// <summary>
/// SpreadJS spreadsheet editor integration.
/// Bundles the "celbridge.spreadsheet" package.
/// </summary>
public class Module : IModule
{
    private const string PackageFolderName = "Package";
    private const string LibraryFolderName = "lib";
    private const string SpreadJSLicenseKeyName = "spreadjs_license_key";
    private const string SpreadJSDesignerLicenseKeyName = "spreadjs_designer_license_key";

    public IReadOnlyList<string> SupportedActivities { get; } = new List<string>()
    {
        nameof(SpreadsheetActivity)
    };

    public void ConfigureServices(IModuleServiceCollection services)
    {
        services.AddTransient<SpreadsheetActivity>();
        services.AddTransient<SpreadsheetEditor>();
        services.AddSingleton<ISpreadsheetReader, SpreadsheetReader>();

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

    public IReadOnlyList<BundledPackageDescriptor> GetBundledPackages()
    {
        var packageFolder = Path.Combine(AppContext.BaseDirectory, "Celbridge.Spreadsheet", PackageFolderName);

        // The public GitHub repo does not include the SpreadJS library files because we don't
        // have a license to distribute them. If the library is not present, we skip
        // registering the package.
        var libraryFolder = Path.Combine(packageFolder, LibraryFolderName);
        var fileSystem = ServiceLocator.AcquireService<ILocalFileSystem>();

        var libraryInfoResult = SyncRunner.Run(() => fileSystem.GetInfoAsync(libraryFolder));
        bool libraryFolderExists = libraryInfoResult.IsSuccess
            && libraryInfoResult.Value.Kind == StorageItemKind.Folder;

        bool isLibraryPresent = false;
        if (libraryFolderExists)
        {
            var enumerateResult = SyncRunner.Run(() => fileSystem.EnumerateAsync(libraryFolder, "*.js", recursive: true));
            isLibraryPresent = enumerateResult.IsSuccess
                && enumerateResult.Value.Any(entry => !entry.IsFolder);
        }

        if (!isLibraryPresent)
        {
            var logger = ServiceLocator.AcquireService<ILogger<Module>>();
            logger.LogInformation("SpreadJS library not found under '{LibraryFolder}'; skipping celbridge.spreadsheet package registration", libraryFolder);

            return Array.Empty<BundledPackageDescriptor>();
        }

        var secrets = new Dictionary<string, string>
        {
            [SpreadJSLicenseKeyName] = SpreadsheetLicenseKeys.LicenseKey,
            [SpreadJSDesignerLicenseKeyName] = SpreadsheetLicenseKeys.DesignerLicenseKey,
        };

        return new[]
        {
            // SpreadJS's licence is domain-locked to spreadjs.celbridge, so its page cannot run from the
            // shared loopback origin. SyntheticOriginHost pins it to that host; every other editor loopback-serves.
            new BundledPackageDescriptor
            {
                Folder = packageFolder,
                Secrets = secrets,
                DevToolsBlocked = true,
                SyntheticOriginHost = "spreadjs.celbridge",
            }
        };
    }
}
