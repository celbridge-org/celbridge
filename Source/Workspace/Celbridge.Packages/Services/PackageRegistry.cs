using Celbridge.Documents;
using Celbridge.Logging;
using Celbridge.Modules;
using Celbridge.Resources;
using Celbridge.Settings;
using Celbridge.Workspace;

namespace Celbridge.Packages;

/// <summary>
/// Discovers, stores, and queries packages from bundled and project sources.
/// </summary>
public class PackageRegistry
{
    // Editors like the code editor can handle 150+ extensions; listing them all
    // makes the discovery log unreadable. Above this count we elide to a count.
    private const int MaxInlineExtensionsInLog = 20;

    private readonly ILogger<PackageRegistry> _logger;
    private readonly IModuleService _moduleService;
    private readonly IFeatureFlags _featureFlags;
    private readonly IPackageLocalizationService _localizationService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ILocalFileSystem _fileSystem;

    private List<Package> _bundledPackages = [];
    private List<Package> _projectPackages = [];

    // Failures from the most recent discovery pass, retained so package_status
    // can report them after load (the error banner only fires once).
    private IReadOnlyList<PackageLoadFailure> _lastFailures = Array.Empty<PackageLoadFailure>();

    // Bundled packages live outside any IResourceRegistry root so their reads
    // stay on direct File.* IO via the gateway. One reader is reused across
    // the discovery and template-fetch paths to keep the bundled branch cheap.
    private readonly IPackageReader _bundledReader;

    public PackageRegistry(
        ILogger<PackageRegistry> logger,
        IModuleService moduleService,
        IFeatureFlags featureFlags,
        IPackageLocalizationService localizationService,
        IWorkspaceWrapper workspaceWrapper,
        ILocalFileSystem fileSystem)
    {
        _logger = logger;
        _moduleService = moduleService;
        _featureFlags = featureFlags;
        _localizationService = localizationService;
        _workspaceWrapper = workspaceWrapper;
        _fileSystem = fileSystem;
        _bundledReader = new DirectPackageReader(fileSystem);
    }

    public async Task<PackageDiscoveryReport> DiscoverPackagesAsync(string projectFolderPath)
    {
        _bundledPackages.Clear();
        _projectPackages.Clear();

        var bundledFailures = DiscoverBundledPackages();
        var projectFailures = await DiscoverProjectPackagesAsync(projectFolderPath);

        var failures = new List<PackageLoadFailure>(bundledFailures.Count + projectFailures.Count);
        failures.AddRange(bundledFailures);
        failures.AddRange(projectFailures);

        var report = new PackageDiscoveryReport
        {
            BundledPackageCount = _bundledPackages.Count,
            ProjectPackageCount = _projectPackages.Count,
            Failures = failures.AsReadOnly()
        };

        _lastFailures = report.Failures;

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

    public IReadOnlyList<PackageLoadFailure> GetLoadFailures()
    {
        return _lastFailures;
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
        // Custom contribution editor IDs are formatted as "{packageName}.{contributionId}"
        // by CustomDocumentViewFactory. Bundled package names themselves contain
        // dots (e.g. "celbridge.notes"), so match by full-name prefix rather than
        // splitting on the first separator.
        var editorIdString = editorId.ToString();
        foreach (var package in GetAllPackages())
        {
            var packageName = package.Info.Name;
            if (packageName.Length == 0)
            {
                continue;
            }

            if (editorIdString.Length > packageName.Length
                && editorIdString.StartsWith(packageName, StringComparison.Ordinal)
                && editorIdString[packageName.Length] == '.')
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

            var localizationStrings = _localizationService.LoadStrings(contribution.Package);
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

    // Reads the default template bytes for the given file extension, picking the
    // first contribution that handles the extension and declares a default template.
    // The reader is chosen by package origin: bundled packages stay on direct File.*
    // because their bytes live outside any registry root, project packages route
    // through IResourceFileSystem by reverse-resolving the template path.
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
            var reader = GetReaderForPackage(contribution.Package);

            if (!reader.Exists(templatePath))
            {
                _logger.LogWarning($"Template file not found: {templatePath}");
                continue;
            }

            var readResult = reader.ReadAllBytes(templatePath);
            if (readResult.IsFailure)
            {
                _logger.LogWarning($"Failed to read template file: {templatePath}. {readResult.FirstErrorMessage}");
                continue;
            }

            return readResult.Value;
        }

        return null;
    }

    // Rejects packages whose document-type registration declares any file
    // extension inside the reserved .cel sidecar namespace. The check runs
    // after the manifest parses cleanly so the failure reason names the
    // offending extension rather than a generic parse error.
    private Result CheckReservedExtensions(Package package)
    {
        var sidecarService = _workspaceWrapper.WorkspaceService.ResourceService.Sidecars;
        foreach (var documentEditor in package.DocumentEditors)
        {
            foreach (var fileType in documentEditor.FileTypes)
            {
                if (sidecarService.IsSidecarFileName(fileType.FileExtension))
                {
                    return Result.Fail(
                        $"Package '{package.Info.Name}' declares document-file-type extension '{fileType.FileExtension}'. "
                        + $"The .cel namespace is reserved for project metadata sidecars.");
                }
            }
        }
        return Result.Ok();
    }

    // Selects the file-read primitive that matches a package's discovery origin.
    // Project packages are read through the gateway; bundled packages stay on
    // direct File.* IO. The project reader is constructed on demand because the
    // workspace-scoped IResourceFileSystem and IResourceRegistry must be looked up at
    // call time rather than cached.
    private IPackageReader GetReaderForPackage(PackageInfo package)
    {
        if (package.Origin == PackageOrigin.Project)
        {
            var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
            var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
            return new ResourceFileSystemPackageReader(resourceFileSystem, resourceRegistry);
        }

        return _bundledReader;
    }

    private List<PackageLoadFailure> DiscoverBundledPackages()
    {
        var failures = new List<PackageLoadFailure>();
        var descriptors = _moduleService.GetBundledPackages();
        var candidates = new List<Package>();

        foreach (var descriptor in descriptors)
        {
            var manifestPath = Path.Combine(descriptor.Folder, PackageConstants.ManifestFileName);
            if (!_bundledReader.Exists(manifestPath))
            {
                // A bundled package with no manifest is a build-time error.
                // Either the descriptor folder is wrong or the package content
                // did not ship with the module.
                _logger.LogError($"Bundled package is missing its manifest: {manifestPath}");
                failures.Add(new PackageLoadFailure
                {
                    Folder = descriptor.Folder,
                    PackageName = null,
                    Reason = PackageLoadFailureReason.InvalidManifest,
                    Detail = $"The package manifest file is missing: {manifestPath}"
                });
                continue;
            }

            var loadResult = PackageManifestLoader.LoadPackage(
                manifestPath,
                descriptor.Secrets,
                descriptor.DevToolsBlocked,
                origin: PackageOrigin.Bundled,
                reader: _bundledReader);
            if (loadResult.IsFailure)
            {
                _logger.LogError(loadResult, $"Failed to load bundled package: {manifestPath}");
                failures.Add(new PackageLoadFailure
                {
                    Folder = descriptor.Folder,
                    PackageName = null,
                    Reason = PackageLoadFailureReason.InvalidManifest,
                    Detail = loadResult.FirstErrorMessage
                });
                continue;
            }

            var package = loadResult.Value;

            var reservedExtensionCheck = CheckReservedExtensions(package);
            if (reservedExtensionCheck.IsFailure)
            {
                _logger.LogError(reservedExtensionCheck, $"Bundled package uses reserved extension: {manifestPath}");
                failures.Add(new PackageLoadFailure
                {
                    Folder = descriptor.Folder,
                    PackageName = package.Info.Name,
                    Reason = PackageLoadFailureReason.ReservedExtension,
                    Detail = reservedExtensionCheck.FirstErrorMessage
                });
                continue;
            }

            candidates.Add(package);
        }

        // Any group of bundled packages that share a name is a first-party build bug.
        // Skip every colliding package so the conflict is visible rather than silently
        // picking a winner, and log at Error level so CI and developers notice.
        foreach (var group in candidates.GroupBy(p => p.Info.Name, StringComparer.Ordinal))
        {
            var members = group.ToList();
            if (members.Count > 1)
            {
                _logger.LogError(
                    $"Multiple bundled packages declare name '{group.Key}'. All {members.Count} instances skipped.");
                foreach (var member in members)
                {
                    failures.Add(new PackageLoadFailure
                    {
                        Folder = member.Info.PackageFolder,
                        PackageName = group.Key,
                        Reason = PackageLoadFailureReason.DuplicateName
                    });
                }
                continue;
            }

            _bundledPackages.Add(members[0]);
        }

        return failures;
    }

    private async Task<List<PackageLoadFailure>> DiscoverProjectPackagesAsync(string projectFolderPath)
    {
        var failures = new List<PackageLoadFailure>();

        if (string.IsNullOrEmpty(projectFolderPath))
        {
            return failures;
        }

        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var projectReader = new ResourceFileSystemPackageReader(resourceFileSystem, resourceRegistry);

        // Walk the project's visible resource set for package.toml manifests. A
        // manifest is a package wherever it lives under the project root. The
        // file-system gateway applies the project's ignore rules, so excluded
        // content (vendored assets, build output) is never scanned. Descent stops
        // at each package root so a package's own content is not searched for
        // nested manifests.
        var manifestResources = new List<ResourceKey>();
        await CollectProjectManifestsAsync(resourceFileSystem, ResourceKey.Empty, manifestResources);

        var candidates = new List<Package>();

        foreach (var manifestResource in manifestResources)
        {
            var resolveResult = resourceRegistry.ResolveResourcePath(manifestResource);
            if (resolveResult.IsFailure)
            {
                continue;
            }
            var manifestPath = resolveResult.Value;
            var packageFolder = Path.GetDirectoryName(manifestPath)!;

            // Project packages are served over the loopback file server; their contribution editors run on
            // every head.
            var loadResult = PackageManifestLoader.LoadPackage(
                manifestPath,
                secrets: null,
                origin: PackageOrigin.Project,
                reader: projectReader);
            if (loadResult.IsFailure)
            {
                _logger.LogWarning(loadResult, $"Skipping invalid project package: {manifestPath}");
                failures.Add(new PackageLoadFailure
                {
                    Folder = packageFolder,
                    PackageName = null,
                    Reason = PackageLoadFailureReason.InvalidManifest,
                    Detail = loadResult.FirstErrorMessage
                });
                continue;
            }

            var package = loadResult.Value;

            var reservedExtensionCheck = CheckReservedExtensions(package);
            if (reservedExtensionCheck.IsFailure)
            {
                _logger.LogWarning(reservedExtensionCheck, $"Skipping project package using reserved extension: {manifestPath}");
                failures.Add(new PackageLoadFailure
                {
                    Folder = packageFolder,
                    PackageName = package.Info.Name,
                    Reason = PackageLoadFailureReason.ReservedExtension,
                    Detail = reservedExtensionCheck.FirstErrorMessage
                });
                continue;
            }

            // The "celbridge." name namespace is reserved for first-party packages
            // shipped inside Celbridge module DLLs. Project packages that try to
            // claim a reserved name are rejected so they cannot impersonate a
            // bundled package in logs, diagnostics, or resource lookups.
            if (package.Info.Name.StartsWith(PackageConstants.ReservedNamePrefix, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    $"Skipping project package with reserved '{PackageConstants.ReservedNamePrefix}' name prefix: {package.Info.Name}");
                failures.Add(new PackageLoadFailure
                {
                    Folder = packageFolder,
                    PackageName = package.Info.Name,
                    Reason = PackageLoadFailureReason.ReservedNamePrefix
                });
                continue;
            }

            // Any other dotted name claims a namespace whose ownership a registry
            // would need to validate. Until such a registry exists, project
            // packages must use flat global-namespace names. Allowing arbitrary
            // dotted names now would let them collide with future registered
            // namespaces once the registry is introduced.
            if (package.Info.Name.Contains('.'))
            {
                _logger.LogWarning(
                    $"Skipping project package '{package.Info.Name}' with dotted name: no namespace registry is available to validate the prefix.");
                failures.Add(new PackageLoadFailure
                {
                    Folder = packageFolder,
                    PackageName = package.Info.Name,
                    Reason = PackageLoadFailureReason.UnregisteredNamespace
                });
                continue;
            }

            if (_bundledPackages.Any(b => b.Info.Name.Equals(package.Info.Name, StringComparison.Ordinal)))
            {
                _logger.LogWarning(
                    $"Skipping project package '{package.Info.Name}' because its name conflicts with a bundled package.");
                failures.Add(new PackageLoadFailure
                {
                    Folder = packageFolder,
                    PackageName = package.Info.Name,
                    Reason = PackageLoadFailureReason.DuplicateName
                });
                continue;
            }

            candidates.Add(package);
        }

        // When two project packages share a name we cannot tell the legitimate
        // one from an impostor, so skip every colliding package rather than pick
        // a winner based on filesystem ordering. The user sees a missing editor,
        // investigates, and resolves the conflict.
        foreach (var group in candidates.GroupBy(p => p.Info.Name, StringComparer.Ordinal))
        {
            var members = group.ToList();
            if (members.Count > 1)
            {
                _logger.LogWarning(
                    $"Multiple project packages declare name '{group.Key}'. All {members.Count} instances skipped to avoid ambiguity.");
                foreach (var member in members)
                {
                    failures.Add(new PackageLoadFailure
                    {
                        Folder = member.Info.PackageFolder,
                        PackageName = group.Key,
                        Reason = PackageLoadFailureReason.DuplicateName
                    });
                }
                continue;
            }

            _projectPackages.Add(members[0]);
        }

        return failures;
    }

    // Recursively collects every package.toml manifest in the project's visible
    // resource set. Enumeration goes through the file-system gateway, which
    // honours the project's ignore rules, so excluded content is never visited.
    // A folder that directly holds a manifest is a package root: its manifest is
    // recorded and the walk does not descend into it, so a package's own files
    // (which may legitimately vendor another package's folder) are not scanned.
    private async Task CollectProjectManifestsAsync(
        IResourceFileSystem resourceFileSystem,
        ResourceKey folderResource,
        List<ResourceKey> manifestResources)
    {
        var enumerateResult = await resourceFileSystem.EnumerateFolderAsync(folderResource);
        if (enumerateResult.IsFailure)
        {
            return;
        }
        var items = enumerateResult.Value;

        var manifestItem = items.FirstOrDefault(item =>
            !item.IsFolder
            && string.Equals(item.Resource.ResourceName, PackageConstants.ManifestFileName, StringComparison.OrdinalIgnoreCase));
        if (manifestItem is not null)
        {
            manifestResources.Add(manifestItem.Resource);
            return;
        }

        foreach (var item in items)
        {
            if (item.IsFolder)
            {
                await CollectProjectManifestsAsync(resourceFileSystem, item.Resource, manifestResources);
            }
        }
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
                $"Discovered {source} package '{package.Info.Name}' (no document editors)");
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
            $"Discovered {source} package '{package.Info.Name}' ({editorCount} {editorLabel}: {editorList})");
    }
}
