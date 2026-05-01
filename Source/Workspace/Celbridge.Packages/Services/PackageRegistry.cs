using Celbridge.Documents;
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
    private const string ReservedIdPrefix = "celbridge.";

    // Editors like the code editor can handle 150+ extensions; listing them all
    // makes the discovery log unreadable. Above this count we elide to a count.
    private const int MaxInlineExtensionsInLog = 20;

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

    public PackageDiscoveryReport DiscoverPackages(string projectFolderPath)
    {
        _bundledPackages.Clear();
        _projectPackages.Clear();

        var bundledFailures = DiscoverBundledPackages();
        var projectFailures = DiscoverProjectPackages(projectFolderPath);

        var failures = new List<PackageLoadFailure>(bundledFailures.Count + projectFailures.Count);
        failures.AddRange(bundledFailures);
        failures.AddRange(projectFailures);

        var report = new PackageDiscoveryReport
        {
            BundledPackageCount = _bundledPackages.Count,
            ProjectPackageCount = _projectPackages.Count,
            Failures = failures.AsReadOnly()
        };

        LogDiscoveredPackages();

        _logger.LogInformation(
            $"Package discovery complete: {report.BundledPackageCount} bundled, {report.ProjectPackageCount} project, {report.Failures.Count} failed");

        return report;
    }

    public IReadOnlyList<Package> GetAllPackages()
    {
        var combined = new List<Package>(_bundledPackages.Count + _projectPackages.Count);
        combined.AddRange(_bundledPackages);
        combined.AddRange(_projectPackages);
        return combined.AsReadOnly();
    }

    public IReadOnlyList<DocumentEditorContribution> GetAllDocumentEditors()
    {
        return GetAllPackages()
            .SelectMany(package => package.DocumentEditors)
            .ToList()
            .AsReadOnly();
    }

    public Package? GetContributingPackage(DocumentEditorId editorId)
    {
        // Custom contribution editor IDs are formatted as "{packageId}.{contributionId}"
        // by CustomDocumentViewFactory. Package IDs themselves contain dots
        // (e.g. "celbridge.notes"), so match by full-id prefix rather than
        // splitting on the first separator.
        var editorIdString = editorId.ToString();
        foreach (var package in GetAllPackages())
        {
            var packageId = package.Info.Id;
            if (packageId.Length == 0)
            {
                continue;
            }

            if (editorIdString.Length > packageId.Length
                && editorIdString.StartsWith(packageId, StringComparison.Ordinal)
                && editorIdString[packageId.Length] == '.')
            {
                return package;
            }
        }

        return null;
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
            var displayKey = contribution.FileTypes[0].DisplayName;
            string displayName;
            if (localizationStrings.TryGetValue(displayKey, out var localizedName))
            {
                displayName = localizedName;
            }
            else
            {
                displayName = displayKey;
            }

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
                return File.ReadAllBytes(templatePath);
            }
            catch (Exception exception)
            {
                _logger.LogWarning($"Failed to read template file: {templatePath}. {exception.Message}");
                continue;
            }
        }

        return null;
    }

    private List<PackageLoadFailure> DiscoverBundledPackages()
    {
        var failures = new List<PackageLoadFailure>();
        var descriptors = _moduleService.GetBundledPackages();
        var candidates = new List<Package>();

        foreach (var descriptor in descriptors)
        {
            var manifestPath = Path.Combine(descriptor.Folder, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                // A bundled package with no manifest is a build-time error.
                // Either the descriptor folder is wrong or the package content
                // did not ship with the module.
                _logger.LogError($"Bundled package is missing its manifest: {manifestPath}");
                failures.Add(new PackageLoadFailure
                {
                    Folder = descriptor.Folder,
                    PackageId = null,
                    Reason = PackageLoadFailureReason.InvalidManifest
                });
                continue;
            }

            var loadResult = PackageManifestLoader.LoadPackage(
                manifestPath,
                descriptor.HostNameOverride,
                descriptor.Secrets,
                descriptor.DevToolsBlocked);
            if (loadResult.IsFailure)
            {
                _logger.LogError(loadResult, $"Failed to load bundled package: {manifestPath}");
                failures.Add(new PackageLoadFailure
                {
                    Folder = descriptor.Folder,
                    PackageId = null,
                    Reason = PackageLoadFailureReason.InvalidManifest
                });
                continue;
            }

            var package = loadResult.Value;
            candidates.Add(package);
        }

        // Any group of bundled packages that share an id is a first-party build bug.
        // Skip every colliding package so the conflict is visible rather than silently
        // picking a winner, and log at Error level so CI and developers notice.
        foreach (var group in candidates.GroupBy(p => p.Info.Id, StringComparer.Ordinal))
        {
            var members = group.ToList();
            if (members.Count > 1)
            {
                _logger.LogError(
                    $"Multiple bundled packages declare id '{group.Key}'. All {members.Count} instances skipped.");
                foreach (var member in members)
                {
                    failures.Add(new PackageLoadFailure
                    {
                        Folder = member.Info.PackageFolder,
                        PackageId = group.Key,
                        Reason = PackageLoadFailureReason.DuplicateId
                    });
                }
                continue;
            }

            _bundledPackages.Add(members[0]);
        }

        return failures;
    }

    private List<PackageLoadFailure> DiscoverProjectPackages(string projectFolderPath)
    {
        var failures = new List<PackageLoadFailure>();

        if (string.IsNullOrEmpty(projectFolderPath))
        {
            return failures;
        }

        var packagesFolder = Path.Combine(projectFolderPath, PackagesFolderName);
        if (!Directory.Exists(packagesFolder))
        {
            return failures;
        }

        var packageFolders = Directory.GetDirectories(packagesFolder);
        var candidates = new List<Package>();

        foreach (var packageFolder in packageFolders)
        {
            var manifestPath = Path.Combine(packageFolder, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                // A folder under packages/ with no manifest is not a package.
                // Silently skip rather than report as a failure.
                continue;
            }

            var loadResult = PackageManifestLoader.LoadPackage(manifestPath, hostNameOverride: null, secrets: null);
            if (loadResult.IsFailure)
            {
                _logger.LogWarning(loadResult, $"Skipping invalid project package: {manifestPath}");
                failures.Add(new PackageLoadFailure
                {
                    Folder = packageFolder,
                    PackageId = null,
                    Reason = PackageLoadFailureReason.InvalidManifest
                });
                continue;
            }

            var package = loadResult.Value;

            // The "celbridge." id namespace is reserved for first-party packages
            // shipped inside Celbridge module DLLs. Project packages that try to
            // claim a reserved id are rejected so they cannot impersonate a
            // bundled package in logs, diagnostics, or resource lookups.
            if (package.Info.Id.StartsWith(ReservedIdPrefix, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    $"Skipping project package with reserved '{ReservedIdPrefix}' id prefix: {package.Info.Id}");
                failures.Add(new PackageLoadFailure
                {
                    Folder = packageFolder,
                    PackageId = package.Info.Id,
                    Reason = PackageLoadFailureReason.ReservedIdPrefix
                });
                continue;
            }

            // Any other dotted id claims a namespace whose ownership a registry
            // would need to validate. Until such a registry exists, project
            // packages must use flat global-namespace ids. Allowing arbitrary
            // dotted ids now would let them collide with future registered
            // namespaces once the registry is introduced.
            if (package.Info.Id.Contains('.'))
            {
                _logger.LogWarning(
                    $"Skipping project package '{package.Info.Id}' with dotted id: no namespace registry is available to validate the prefix.");
                failures.Add(new PackageLoadFailure
                {
                    Folder = packageFolder,
                    PackageId = package.Info.Id,
                    Reason = PackageLoadFailureReason.UnregisteredNamespace
                });
                continue;
            }

            if (_bundledPackages.Any(b => b.Info.Id.Equals(package.Info.Id, StringComparison.Ordinal)))
            {
                _logger.LogWarning(
                    $"Skipping project package '{package.Info.Id}' because its id conflicts with a bundled package.");
                failures.Add(new PackageLoadFailure
                {
                    Folder = packageFolder,
                    PackageId = package.Info.Id,
                    Reason = PackageLoadFailureReason.DuplicateId
                });
                continue;
            }

            candidates.Add(package);
        }

        // When two project packages share an id we cannot tell the legitimate
        // one from an impostor, so skip every colliding package rather than pick
        // a winner based on filesystem ordering. The user sees a missing editor,
        // investigates, and resolves the conflict.
        foreach (var group in candidates.GroupBy(p => p.Info.Id, StringComparer.Ordinal))
        {
            var members = group.ToList();
            if (members.Count > 1)
            {
                _logger.LogWarning(
                    $"Multiple project packages declare id '{group.Key}'. All {members.Count} instances skipped to avoid ambiguity.");
                foreach (var member in members)
                {
                    failures.Add(new PackageLoadFailure
                    {
                        Folder = member.Info.PackageFolder,
                        PackageId = group.Key,
                        Reason = PackageLoadFailureReason.DuplicateId
                    });
                }
                continue;
            }

            _projectPackages.Add(members[0]);
        }

        return failures;
    }

    // Runs after dedup so that duplicate-id packages rejected by the group
    // passes do not appear in the log. Emits one Info-level line per accepted
    // package so a support reader can tell at a glance which packages loaded,
    // whether each is bundled or project-provided, and which file extensions
    // each document editor handles.
    private void LogDiscoveredPackages()
    {
        foreach (var package in _bundledPackages)
        {
            LogDiscoveredPackage(package, "bundled");
        }

        foreach (var package in _projectPackages)
        {
            LogDiscoveredPackage(package, "project");
        }
    }

    private void LogDiscoveredPackage(Package package, string source)
    {
        var editorCount = package.DocumentEditors.Count;

        if (editorCount == 0)
        {
            _logger.LogInformation(
                $"Discovered {source} package '{package.Info.Id}' (no document editors)");
            return;
        }

        var editorDescriptions = new List<string>(editorCount);
        foreach (var editor in package.DocumentEditors)
        {
            var extensionCount = editor.FileTypes.Count;
            string extensionList;
            if (extensionCount > MaxInlineExtensionsInLog)
            {
                extensionList = $"{extensionCount} extensions";
            }
            else
            {
                extensionList = string.Join(", ", editor.FileTypes.Select(ft => ft.FileExtension));
            }
            editorDescriptions.Add($"{editor.Id} [{extensionList}]");
        }

        var editorLabel = editorCount == 1 ? "document editor" : "document editors";
        var editorList = string.Join("; ", editorDescriptions);

        _logger.LogInformation(
            $"Discovered {source} package '{package.Info.Id}' ({editorCount} {editorLabel}: {editorList})");
    }
}
