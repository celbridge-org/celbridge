using Celbridge.Activities;
using Celbridge.Logging;
using Celbridge.Modules;
using Celbridge.Packages;
using Celbridge.Screenplay.Components;
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
            return Result<IActivity>.Ok(activity);
        }

        return Result<IActivity>.Fail();
    }

    public IReadOnlyList<BundledPackageDescriptor> GetBundledPackages()
    {
        var packageFolder = Path.Combine(AppContext.BaseDirectory, "Celbridge.Spreadsheet", PackageFolderName);

        // The public GitHub repo does not include the SpreadJS library files because we don't
        // have a license to distribute them. If the library is not present, we skip
        // registering the package.
        var libraryFolder = Path.Combine(packageFolder, LibraryFolderName);
        var isLibraryPresent = Directory.Exists(libraryFolder) &&
                               Directory.EnumerateFiles(libraryFolder, "*.js", SearchOption.AllDirectories).Any();
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
            new BundledPackageDescriptor
            {
                Folder = packageFolder,
                HostNameOverride = "spreadjs.celbridge",
                Secrets = secrets,
                DevToolsBlocked = true,
            }
        };
    }
}
