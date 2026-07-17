using Celbridge.Documents;
using Celbridge.Logging;
using Celbridge.Modules;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Workspace;

namespace Celbridge.Packages;

/// <summary>
/// Discovers packages from bundled and project sources, applies the project's activation list,
/// and resolves the declared editor instances and built-in editors against the discovered
/// contributions.
/// </summary>
public class PackageRegistry
{
    // Editors like the code editor can handle 150+ extensions. Listing them all
    // makes the discovery log unreadable. Above this count we elide to a count.
    private const int MaxInlineExtensionsInLog = 20;

    private readonly ILogger<PackageRegistry> _logger;
    private readonly IModuleService _moduleService;
    private readonly IPackageLocalizationService _localizationService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectService _projectService;
    private readonly ILocalFileSystem _fileSystem;

    private List<Package> _bundledPackages = [];
    private List<Package> _projectPackages = [];

    private List<EditorInstance> _editorInstances = [];
    private List<EditorInstance> _builtInEditors = [];

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
        IPackageLocalizationService localizationService,
        IWorkspaceWrapper workspaceWrapper,
        IProjectService projectService,
        ILocalFileSystem fileSystem)
    {
        _logger = logger;
        _moduleService = moduleService;
        _localizationService = localizationService;
        _workspaceWrapper = workspaceWrapper;
        _projectService = projectService;
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

        var instanceFailures = new List<EditorInstanceLoadFailure>();
        var instanceWarnings = new List<EditorInstanceLoadFailure>();
        ResolveEditorInstances(instanceFailures, instanceWarnings);
        ResolveBuiltInEditors();

        var report = new PackageDiscoveryReport
        {
            BundledPackageCount = _bundledPackages.Count,
            ProjectPackageCount = _projectPackages.Count,
            Failures = failures.AsReadOnly(),
            EditorInstanceCount = _editorInstances.Count,
            EditorInstanceFailures = instanceFailures.AsReadOnly(),
            EditorInstanceWarnings = instanceWarnings.AsReadOnly()
        };

        _lastFailures = report.Failures;

        LogDiscoveredPackages();

        _logger.LogInformation(
            $"Package discovery complete: {report.BundledPackageCount} bundled, {report.ProjectPackageCount} project, {report.Failures.Count} failed, {report.EditorInstanceCount} instances");

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

    public IReadOnlyList<EditorContribution> GetAllEditors()
    {
        return GetAllPackages()
            .SelectMany(package => package.Editors)
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyList<EditorInstance> GetEditorInstances()
    {
        return _editorInstances.AsReadOnly();
    }

    public IReadOnlyList<EditorInstance> GetBuiltInEditors()
    {
        return _builtInEditors.AsReadOnly();
    }

    public Package? GetContributingPackage(EditorInstanceId editorId)
    {
        var editor = FindEditor(editorId);
        if (editor is null)
        {
            return null;
        }

        var packageName = editor.Contribution.Package.Name;

        return GetAllPackages().FirstOrDefault(p => p.Info.Name.Equals(packageName, StringComparison.Ordinal));
    }

    public IReadOnlyList<DocumentTypeInfo> GetDocumentTypes()
    {
        var documentTypes = new List<DocumentTypeInfo>();
        var seenContributions = new HashSet<EditorContribution>();

        foreach (var contribution in GetAvailableContributions())
        {
            // A utility owns per-instance state files and is never created as an ordinary project
            // file, so it must not appear as a creatable type in the New File dialog.
            if (contribution.IsUtility)
            {
                continue;
            }

            if (contribution.Templates.Count == 0)
            {
                continue;
            }

            // Multiple instances of one contribution offer one document type.
            if (!seenContributions.Add(contribution))
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
    // first available contribution that handles the extension and declares a default template.
    // The reader is chosen by package origin: bundled packages stay on direct File.*
    // because their bytes live outside any registry root, project packages route
    // through IResourceFileSystem by reverse-resolving the template path.
    public byte[]? GetDefaultTemplateContent(string fileExtension)
    {
        var normalizedExtension = fileExtension.ToLowerInvariant();

        foreach (var contribution in GetAvailableContributions())
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

    // Reads the seed template bytes for a utility contribution from its manifest 'template'
    // path, choosing the reader by package origin (direct File.* for bundled, gateway for
    // project). Returns an empty array when the utility declares no template, and null when a
    // declared template file is missing or unreadable so callers can log and seed an empty file.
    public byte[]? GetUtilityTemplateContent(EditorContribution contribution)
    {
        var descriptor = contribution.UtilityDescriptor;
        if (descriptor is null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(descriptor.Template))
        {
            return Array.Empty<byte>();
        }

        var templatePath = Path.Combine(contribution.Package.PackageFolder, descriptor.Template);
        var reader = GetReaderForPackage(contribution.Package);

        if (!reader.Exists(templatePath))
        {
            _logger.LogWarning($"Utility template file not found: {templatePath}");
            return null;
        }

        var readResult = reader.ReadAllBytes(templatePath);
        if (readResult.IsFailure)
        {
            _logger.LogWarning($"Failed to read utility template file: {templatePath}. {readResult.FirstErrorMessage}");
            return null;
        }

        return readResult.Value;
    }

    // Resolves the project's declared instance tables against the activated packages, preserving
    // declaration order. Skipped entries and dropped config keys are reported so the load path can
    // surface them as console banners.
    private void ResolveEditorInstances(
        List<EditorInstanceLoadFailure> instanceFailures,
        List<EditorInstanceLoadFailure> instanceWarnings)
    {
        _editorInstances = [];

        var config = _projectService.CurrentProject?.Config;
        if (config is null)
        {
            return;
        }

        var activatedPackages = new HashSet<string>(config.Celbridge.Packages, StringComparer.Ordinal);

        foreach (var activationEntry in activatedPackages)
        {
            if (!GetAllPackages().Any(p => p.Info.Name.Equals(activationEntry, StringComparison.Ordinal)))
            {
                _logger.LogWarning($"Activated package '{activationEntry}' was not discovered.");
            }
        }

        foreach (var declaration in config.Instances)
        {
            var package = GetAllPackages()
                .FirstOrDefault(p => p.Info.Name.Equals(declaration.PackageName, StringComparison.Ordinal));
            if (package is null)
            {
                instanceFailures.Add(new EditorInstanceLoadFailure
                {
                    InstanceId = declaration.InstanceId,
                    Detail = $"References unknown package '{declaration.PackageName}'."
                });
                continue;
            }

            // Built-in packages are always active, so an instance may reference them without an
            // activation entry.
            var isActivated = activatedPackages.Contains(package.Info.Name) ||
                BuiltInEditors.IsAlwaysActivePackage(package.Info.Name);
            if (!isActivated)
            {
                instanceFailures.Add(new EditorInstanceLoadFailure
                {
                    InstanceId = declaration.InstanceId,
                    Detail = $"Package '{declaration.PackageName}' is not activated. Add it to [celbridge].packages."
                });
                continue;
            }

            var contribution = package.Editors
                .FirstOrDefault(e => e.Id.Equals(declaration.ContributionId, StringComparison.Ordinal));
            if (contribution is null)
            {
                instanceFailures.Add(new EditorInstanceLoadFailure
                {
                    InstanceId = declaration.InstanceId,
                    Detail = $"Package '{declaration.PackageName}' has no contribution '{declaration.ContributionId}'."
                });
                continue;
            }

            var effectiveConfig = BuildEffectiveConfig(contribution, declaration, out var droppedKeys);
            if (droppedKeys.Count > 0)
            {
                instanceWarnings.Add(new EditorInstanceLoadFailure
                {
                    InstanceId = declaration.InstanceId,
                    Detail = $"Dropped invalid config keys: {string.Join(", ", droppedKeys)}."
                });
            }

            var instance = new EditorInstance
            {
                InstanceId = new EditorInstanceId(declaration.InstanceId),
                Contribution = contribution,
                Config = effectiveConfig,
                Title = declaration.Title,
                Icon = declaration.Icon,
                Tooltip = declaration.Tooltip
            };

            _editorInstances.Add(instance);
        }
    }

    // Wraps the always-active package contributions in EditorInstance records under their
    // host-assigned ids, in catalog order.
    private void ResolveBuiltInEditors()
    {
        _builtInEditors = [];

        foreach (var definition in BuiltInEditors.PackageBuiltIns)
        {
            var package = _bundledPackages
                .FirstOrDefault(p => p.Info.Name.Equals(definition.PackageName, StringComparison.Ordinal));
            var contribution = package?.Editors
                .FirstOrDefault(e => e.Id.Equals(definition.ContributionId, StringComparison.Ordinal));
            if (contribution is null)
            {
                if (definition.Optional)
                {
                    // An optional built-in ships in the installer but is absent from a source build
                    // without its private library, so its file types degrade to having no editor.
                    _logger.LogInformation(
                        $"Optional built-in editor '{definition.EditorId}' is not available: package '{definition.PackageName}' was not discovered.");
                    continue;
                }

                // A missing required built-in is a build or packaging error.
                _logger.LogError(
                    $"Built-in editor '{definition.EditorId}' is missing: package '{definition.PackageName}' has no contribution '{definition.ContributionId}'.");
                continue;
            }

            var builtInInstance = new EditorInstance
            {
                InstanceId = definition.EditorId,
                Contribution = contribution,
                Config = BuildEffectiveConfig(contribution, declaration: null, out _)
            };

            _builtInEditors.Add(builtInInstance);
        }
    }

    // Computes the configuration delivered to an editor's views: the manifest [options] table,
    // overlaid with the contribution's descriptor defaults, overlaid with the instance's
    // type-checked config keys. Keys that fail descriptor type-checking are dropped and reported.
    private IReadOnlyDictionary<string, string> BuildEffectiveConfig(
        EditorContribution contribution,
        EditorInstanceDeclaration? declaration,
        out List<string> droppedKeys)
    {
        droppedKeys = [];

        var effectiveConfig = new Dictionary<string, string>(contribution.Options);

        foreach (var descriptor in contribution.ConfigDescriptors)
        {
            if (descriptor.DefaultValue is not null)
            {
                effectiveConfig[descriptor.Key] = descriptor.DefaultValue;
            }
        }

        if (declaration is null)
        {
            return effectiveConfig;
        }

        foreach (var (key, rawValue) in declaration.Config)
        {
            var descriptor = contribution.ConfigDescriptors
                .FirstOrDefault(d => d.Key.Equals(key, StringComparison.Ordinal));
            if (descriptor is null)
            {
                droppedKeys.Add(key);
                _logger.LogWarning(
                    $"Instance '{declaration.InstanceId}' sets unknown config key '{key}' for contribution '{contribution.Id}'.");
                continue;
            }

            var encodeResult = ConfigValueEncoder.Encode(rawValue, descriptor);
            if (encodeResult.IsFailure)
            {
                droppedKeys.Add(key);
                _logger.LogWarning(
                    $"Instance '{declaration.InstanceId}' config key '{key}' was dropped: {encodeResult.FirstErrorMessage}");
                continue;
            }
            var encodedValue = encodeResult.Value;

            effectiveConfig[key] = encodedValue;
        }

        return effectiveConfig;
    }

    // Finds a declared instance or built-in editor by id.
    private EditorInstance? FindEditor(EditorInstanceId editorId)
    {
        var declaredInstance = _editorInstances.FirstOrDefault(i => i.InstanceId == editorId);
        if (declaredInstance is not null)
        {
            return declaredInstance;
        }

        return _builtInEditors.FirstOrDefault(i => i.InstanceId == editorId);
    }

    // Enumerates the contributions of the available editors: declared instances first (in
    // declaration order), then the built-ins.
    private IEnumerable<EditorContribution> GetAvailableContributions()
    {
        foreach (var instance in _editorInstances)
        {
            yield return instance.Contribution;
        }

        foreach (var builtIn in _builtInEditors)
        {
            yield return builtIn.Contribution;
        }
    }

    // Rejects packages whose document-type registration declares any file
    // extension inside the reserved .cel sidecar namespace.
    private Result CheckReservedExtensions(Package package)
    {
        var sidecarService = _workspaceWrapper.WorkspaceService.ResourceService.Sidecars;
        foreach (var editor in package.Editors)
        {
            foreach (var fileType in editor.FileTypes)
            {
                if (sidecarService.IsSidecarFileName(fileType.FileExtension))
                {
                    return Result.Fail(
                        $"Package '{package.Info.Name}' declares file-type extension '{fileType.FileExtension}'. "
                        + $"The .cel namespace is reserved for project metadata sidecars.");
                }
            }
        }
        return Result.Ok();
    }

    // Selects the file-read primitive that matches a package's discovery origin.
    // Project packages are read through the gateway. Bundled packages stay on
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
        // Skip every colliding package rather than silently picking a winner.
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
        // manifest is a package wherever it lives under the project root.
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

            // Project packages are served over the loopback file server. Their custom editors run on
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
            // packages must use flat global-namespace names.
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
        // a winner based on filesystem ordering.
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

    // Emits one Info-level line per accepted package. Runs after dedup so
    // packages rejected as duplicates do not appear in the log.
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
        var editorCount = package.Editors.Count;

        if (editorCount == 0)
        {
            _logger.LogInformation(
                $"Discovered {source} package '{package.Info.Name}' (no editors)");
            return;
        }

        var editorDescriptions = new List<string>(editorCount);
        foreach (var editor in package.Editors)
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

        var editorLabel = editorCount == 1 ? "editor" : "editors";
        var editorList = string.Join("; ", editorDescriptions);

        _logger.LogInformation(
            $"Discovered {source} package '{package.Info.Name}' ({editorCount} {editorLabel}: {editorList})");
    }
}
