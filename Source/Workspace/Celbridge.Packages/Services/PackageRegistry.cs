using System.Text;
using Celbridge.Logging;
using Celbridge.Modules;
using Celbridge.Settings;

namespace Celbridge.Packages;

/// <summary>
/// Discovers, stores, and queries packages from bundled and project sources.
/// </summary>
public class PackageRegistry
{
    private const string PackagesFolderName = "packages";
    private const string ManifestFileName = "package.toml";

    private readonly ILogger<PackageRegistry> _logger;
    private readonly IModuleService _moduleService;
    private readonly IFeatureFlags _featureFlags;
    private readonly IPackageLocalizationService _localizationService;

    private List<Package> _bundledPackages = [];
    private List<Package> _projectPackages = [];

    public PackageRegistry(
        ILogger<PackageRegistry> logger,
        IModuleService moduleService,
        IFeatureFlags featureFlags,
        IPackageLocalizationService localizationService)
    {
        _logger = logger;
        _moduleService = moduleService;
        _featureFlags = featureFlags;
        _localizationService = localizationService;
    }

    public void DiscoverPackages(string projectFolderPath)
    {
        _bundledPackages.Clear();
        _projectPackages.Clear();

        DiscoverBundledPackages();
        DiscoverProjectPackages(projectFolderPath);
    }

    public IReadOnlyList<Package> GetAllPackages()
    {
        var combined = new List<Package>(_bundledPackages.Count + _projectPackages.Count);
        combined.AddRange(_bundledPackages);
        combined.AddRange(_projectPackages);
        return combined.AsReadOnly();
    }

    public IReadOnlyList<DocumentContribution> GetAllDocumentEditors()
    {
        return GetAllPackages()
            .SelectMany(package => package.DocumentEditors)
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyList<DocumentTypeInfo> GetDocumentTypes()
    {
        var contributions = GetAllDocumentEditors();
        var documentTypes = new List<DocumentTypeInfo>();

        foreach (var contribution in contributions)
        {
            if (contribution.Templates.Count == 0)
            {
                continue;
            }

            var featureFlag = contribution.Package.FeatureFlag;
            if (!string.IsNullOrEmpty(featureFlag) && !_featureFlags.IsEnabled(featureFlag))
            {
                continue;
            }

            var localizationStrings = _localizationService.LoadStrings(contribution.Package.PackageFolder);
            var displayName = ResolveDisplayName(contribution, localizationStrings);
            var fileExtensions = contribution.FileTypes.Select(ft => ft.FileExtension).ToList().AsReadOnly();

            var documentTypeInfo = new DocumentTypeInfo(displayName, fileExtensions);
            documentTypes.Add(documentTypeInfo);
        }

        return documentTypes.AsReadOnly();
    }

    public byte[]? GetDefaultTemplateContent(string fileExtension)
    {
        var normalizedExtension = fileExtension.ToLowerInvariant();
        var contributions = GetAllDocumentEditors();

        foreach (var contribution in contributions)
        {
            var handlesExtension = contribution.FileTypes
                .Any(ft => ft.FileExtension.Equals(normalizedExtension, StringComparison.OrdinalIgnoreCase));

            if (!handlesExtension)
            {
                continue;
            }

            var defaultTemplate = contribution.Templates
                .FirstOrDefault(t => t.Default);

            if (defaultTemplate is null)
            {
                continue;
            }

            var templatePath = Path.Combine(contribution.Package.PackageFolder, defaultTemplate.TemplateFile);
            if (!File.Exists(templatePath))
            {
                _logger.LogWarning($"Template file not found: {templatePath}");
                continue;
            }

            try
            {
                var content = File.ReadAllText(templatePath, Encoding.UTF8);
                return Encoding.UTF8.GetBytes(content);
            }
            catch (Exception exception)
            {
                _logger.LogWarning($"Failed to read template file: {templatePath}. {exception.Message}");
                continue;
            }
        }

        return null;
    }

    private void DiscoverBundledPackages()
    {
        var packageFolders = _moduleService.GetBundledPackageFolders();
        foreach (var packageFolder in packageFolders)
        {
            var package = TryLoadPackage(packageFolder);
            if (package is not null)
            {
                _bundledPackages.Add(package);
            }
        }
    }

    private void DiscoverProjectPackages(string projectFolderPath)
    {
        if (string.IsNullOrEmpty(projectFolderPath))
        {
            return;
        }

        var packagesFolder = Path.Combine(projectFolderPath, PackagesFolderName);

        if (!Directory.Exists(packagesFolder))
        {
            return;
        }

        var packageFolders = Directory.GetDirectories(packagesFolder);
        foreach (var packageFolder in packageFolders)
        {
            var package = TryLoadPackage(packageFolder);
            if (package is not null)
            {
                _projectPackages.Add(package);
            }
        }
    }

    /// <summary>
    /// Attempts to load a package from a folder. Returns null on failure.
    /// </summary>
    private Package? TryLoadPackage(string packageFolder)
    {
        var manifestPath = Path.Combine(packageFolder, ManifestFileName);

        if (!File.Exists(manifestPath))
        {
            return null;
        }

        var loadResult = PackageManifestLoader.LoadPackage(manifestPath);

        if (loadResult.IsFailure)
        {
            _logger.LogWarning(loadResult, $"Skipping invalid package: {manifestPath}");
            return null;
        }

        var package = loadResult.Value;
        foreach (var documentEditor in package.DocumentEditors)
        {
            var contributionType = documentEditor.GetType().Name;
            _logger.LogDebug($"Discovered package document: {documentEditor.Id} ({contributionType}) for {string.Join(", ", documentEditor.FileTypes.Select(ft => ft.FileExtension))}");
        }

        return package;
    }

    /// <summary>
    /// Resolves the display name for a contribution.
    /// Uses the first file type's display name if available, looked up in the localization dictionary.
    /// Falls back to the package name.
    /// </summary>
    private static string ResolveDisplayName(
        DocumentContribution contribution,
        IReadOnlyDictionary<string, string> localizationStrings)
    {
        var firstFileType = contribution.FileTypes.FirstOrDefault();
        if (firstFileType is not null && !string.IsNullOrEmpty(firstFileType.DisplayName))
        {
            if (localizationStrings.TryGetValue(firstFileType.DisplayName, out var localizedName))
            {
                return localizedName;
            }
            return firstFileType.DisplayName;
        }
        return contribution.Package.Name;
    }
}
