using Celbridge.Logging;
using Celbridge.Packages;

namespace Celbridge.Spreadsheet;

/// <summary>
/// Registers the "celbridge-spreadsheet" package when the SpreadJS library is present. The public GitHub
/// repo does not ship the licensed library files, so the package is skipped when they are absent.
/// </summary>
public sealed class SpreadsheetBundledPackageProvider : IBundledPackageProvider
{
    private const string PackageFolderName = "Package";
    private const string LibraryFolderName = "lib";
    private const string SpreadJSLicenseKeyName = "spreadjs_license_key";
    private const string SpreadJSDesignerLicenseKeyName = "spreadjs_designer_license_key";

    private readonly ILocalFileSystem _fileSystem;
    private readonly ILogger<SpreadsheetBundledPackageProvider> _logger;

    public SpreadsheetBundledPackageProvider(
        ILocalFileSystem fileSystem,
        ILogger<SpreadsheetBundledPackageProvider> logger)
    {
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public IReadOnlyList<BundledPackageDescriptor> GetBundledPackages()
    {
        var packageFolder = Path.Combine(AppContext.BaseDirectory, "Celbridge.Spreadsheet", PackageFolderName);
        var libraryFolder = Path.Combine(packageFolder, LibraryFolderName);

        var libraryInfoResult = SyncRunner.Run(() => _fileSystem.GetInfoAsync(libraryFolder));
        bool libraryFolderExists = libraryInfoResult.IsSuccess
            && libraryInfoResult.Value.Kind == StorageItemKind.Folder;

        bool isLibraryPresent = false;
        if (libraryFolderExists)
        {
            var enumerateResult = SyncRunner.Run(() => _fileSystem.EnumerateAsync(libraryFolder, "*.js", recursive: true));
            isLibraryPresent = enumerateResult.IsSuccess
                && enumerateResult.Value.Any(entry => !entry.IsFolder);
        }

        if (!isLibraryPresent)
        {
            _logger.LogInformation("SpreadJS library not found under '{LibraryFolder}'; skipping celbridge-spreadsheet package registration", libraryFolder);

            return Array.Empty<BundledPackageDescriptor>();
        }

        var secrets = new Dictionary<string, string>
        {
            [SpreadJSLicenseKeyName] = SpreadsheetLicenseKeys.LicenseKey,
            [SpreadJSDesignerLicenseKeyName] = SpreadsheetLicenseKeys.DesignerLicenseKey,
        };

        // The licence secrets are injected into the page, so a release build keeps DevTools and the
        // webview_* tools away from it. A debug build is the developer's own machine, where reaching the
        // page is what makes the integration workable.
#if DEBUG
        const bool devToolsBlocked = false;
#else
        const bool devToolsBlocked = true;
#endif

        return new[]
        {
            // SpreadJS's licence is domain-locked, so its page loads under a synthetic origin.
            new BundledPackageDescriptor
            {
                Folder = packageFolder,
                Secrets = secrets,
                DevToolsBlocked = devToolsBlocked,
            }
        };
    }
}
