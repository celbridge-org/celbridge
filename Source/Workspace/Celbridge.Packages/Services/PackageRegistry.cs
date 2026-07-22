using Celbridge.Documents;
using Celbridge.Logging;
using Celbridge.Modules;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.UserInterface;
using Celbridge.Workspace;

namespace Celbridge.Packages;

/// <summary>
/// Discovers packages from bundled and project sources, reconciles the project config against the
/// discovered contributions (materializing default-active editors and honouring disables), and
/// resolves the resulting editors and the built-ins.
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

    private List<Package> _bundledPackages = [];
    private List<Package> _projectPackages = [];

    private List<ResolvedEditor> _resolvedEditors = [];
    private List<ResolvedEditor> _builtInEditors = [];

    // The reconciled, normalized config from the most recent discovery pass. RegisterPackagesAsync
    // persists it; a rescan leaves it unwritten.
    private ProjectConfig? _normalizedConfig;

    // Failures from the most recent discovery pass, retained so package_status
    // can report them after load (the error banner only fires once).
    private IReadOnlyList<PackageLoadFailure> _lastFailures = Array.Empty<PackageLoadFailure>();
    private IReadOnlyList<ContributionIssue> _lastContributionIssues = Array.Empty<ContributionIssue>();

    // Bundled packages live outside any IResourceRegistry root so their reads
    // stay on direct File.* IO via the gateway. One reader is reused across
    // the discovery and template-fetch paths to keep the bundled branch cheap.
    private readonly IPackageReader _bundledReader;

    private readonly IFileTypeCatalog _fileTypeCatalog;
    private readonly IIconService _iconService;

    public PackageRegistry(
        ILogger<PackageRegistry> logger,
        IModuleService moduleService,
        IPackageLocalizationService localizationService,
        IWorkspaceWrapper workspaceWrapper,
        IProjectService projectService,
        ILocalFileSystem fileSystem,
        IFileTypeCatalog fileTypeCatalog,
        IIconService iconService)
    {
        _logger = logger;
        _moduleService = moduleService;
        _localizationService = localizationService;
        _workspaceWrapper = workspaceWrapper;
        _projectService = projectService;
        _fileTypeCatalog = fileTypeCatalog;
        _iconService = iconService;
        _bundledReader = new DirectPackageReader(fileSystem);
    }

    public async Task<PackageDiscoveryReport> DiscoverPackagesAsync(string projectFolderPath, bool persistNormalizedConfig)
    {
        _bundledPackages.Clear();
        _projectPackages.Clear();

        // Manifests that claim their extensions from the catalog need it populated before they load.
        await _fileTypeCatalog.LoadAsync();

        var bundledFailures = DiscoverBundledPackages();
        var projectFailures = await DiscoverProjectPackagesAsync(projectFolderPath);

        var failures = new List<PackageLoadFailure>(bundledFailures.Count + projectFailures.Count);
        failures.AddRange(bundledFailures);
        failures.AddRange(projectFailures);

        var resolvedEditorFailures = new List<ResolvedEditorLoadFailure>();
        var resolvedEditorWarnings = new List<ResolvedEditorLoadFailure>();
        await ResolveResolvedEditorsAsync(persistNormalizedConfig && failures.Count == 0, resolvedEditorFailures, resolvedEditorWarnings);
        ResolveBuiltInEditors();

        var contributionIssues = new List<ContributionIssue>();
        ApplyFileIconOverrides(contributionIssues);
        _lastContributionIssues = contributionIssues.AsReadOnly();

        resolvedEditorWarnings.AddRange(contributionIssues.Select(ToReportWarning));

        var report = new PackageDiscoveryReport
        {
            BundledPackageCount = _bundledPackages.Count,
            ProjectPackageCount = _projectPackages.Count,
            Failures = failures.AsReadOnly(),
            ResolvedEditorCount = _resolvedEditors.Count,
            ResolvedEditorFailures = resolvedEditorFailures.AsReadOnly(),
            ResolvedEditorWarnings = resolvedEditorWarnings.AsReadOnly()
        };

        _lastFailures = report.Failures;

        LogDiscoveredPackages();

        _logger.LogInformation(
            $"Package discovery complete: {report.BundledPackageCount} bundled, {report.ProjectPackageCount} project, {report.Failures.Count} failed, {report.ResolvedEditorCount} resolved editors");

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

    public IReadOnlyList<ContributionIssue> GetContributionIssues()
    {
        return _lastContributionIssues;
    }

    public IReadOnlyList<EditorContribution> GetAllEditors()
    {
        return GetAllPackages()
            .SelectMany(package => package.Editors)
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyList<ResolvedEditor> GetResolvedEditors()
    {
        return _resolvedEditors.AsReadOnly();
    }

    public ProjectConfig? GetNormalizedConfig()
    {
        return _normalizedConfig;
    }

    public IReadOnlyList<ResolvedEditor> GetBuiltInEditors()
    {
        return _builtInEditors.AsReadOnly();
    }

    public Package? GetContributingPackage(EditorId editorId)
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
            // A utility owns its own state file and is never created as an ordinary project
            // file, so it must not appear as a creatable type in the New File dialog.
            if (contribution.IsUtility)
            {
                continue;
            }

            if (contribution.Templates.Count == 0)
            {
                continue;
            }

            // One contribution offers one document type.
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

    // Reconciles the project config against the discovered contributions and builds the resolved editor
    // set from the result. Default-active contributions materialize, disabled contributions and disabled
    // packages drop out, and reconcile warnings surface as advisory banners. On a persisting load the
    // reconcile also writes the normalized config back to the project file.
    private async Task ResolveResolvedEditorsAsync(
        bool persistNormalizedConfig,
        List<ResolvedEditorLoadFailure> resolvedEditorFailures,
        List<ResolvedEditorLoadFailure> resolvedEditorWarnings)
    {
        _resolvedEditors = [];
        _normalizedConfig = null;

        var discoveredContributions = GetAllPackages()
            .SelectMany(package => package.Editors)
            .ToList();

        var reconcileResult = await _projectService.ReconcileConfigAsync(discoveredContributions, persistNormalizedConfig);
        if (reconcileResult is null)
        {
            return;
        }

        _normalizedConfig = reconcileResult.Config;

        foreach (var warning in reconcileResult.Warnings)
        {
            resolvedEditorWarnings.Add(new ResolvedEditorLoadFailure
            {
                EditorId = string.Empty,
                Detail = warning
            });
        }

        foreach (var resolved in reconcileResult.ActiveContributions)
        {
            var contribution = resolved.Contribution;
            var effectiveConfig = BuildEffectiveConfig(contribution, resolved.Config, out _);

            var resolvedEditor = new ResolvedEditor
            {
                EditorId = EditorId.Create(contribution.Package.Name, contribution.Id),
                Contribution = contribution,
                Config = effectiveConfig,
            };

            _resolvedEditors.Add(resolvedEditor);
        }
    }

    // Wraps the always-active package contributions in ResolvedEditor records under their
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

            var builtInEditor = new ResolvedEditor
            {
                EditorId = definition.EditorId,
                Contribution = contribution,
                Config = BuildEffectiveConfig(contribution, rawConfig: null, out _)
            };

            _builtInEditors.Add(builtInEditor);
        }
    }

    // Computes the configuration delivered to an editor's views: the manifest [options] table,
    // overlaid with the contribution's descriptor defaults, overlaid with the project's per-contribution
    // config keys. Keys that fail descriptor type-checking are dropped and reported. rawConfig is null
    // for a built-in editor, which carries no project config.
    private IReadOnlyDictionary<string, string> BuildEffectiveConfig(
        EditorContribution contribution,
        IReadOnlyDictionary<string, object?>? rawConfig,
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

        if (rawConfig is null)
        {
            return effectiveConfig;
        }

        var reference = $"{contribution.Package.Name}/{contribution.Id}";

        foreach (var (key, rawValue) in rawConfig)
        {
            var descriptor = contribution.ConfigDescriptors
                .FirstOrDefault(d => d.Key.Equals(key, StringComparison.Ordinal));
            if (descriptor is null)
            {
                droppedKeys.Add(key);
                _logger.LogWarning(
                    $"Contribution '{reference}' sets unknown config key '{key}'.");
                continue;
            }

            var encodeResult = ConfigValueEncoder.Encode(rawValue, descriptor);
            if (encodeResult.IsFailure)
            {
                droppedKeys.Add(key);
                _logger.LogWarning(
                    $"Contribution '{reference}' config key '{key}' was dropped: {encodeResult.FirstErrorMessage}");
                continue;
            }
            var encodedValue = encodeResult.Value;

            effectiveConfig[key] = encodedValue;
        }

        return effectiveConfig;
    }

    // Finds a declared or built-in editor by id.
    private ResolvedEditor? FindEditor(EditorId editorId)
    {
        var declaredEditor = _resolvedEditors.FirstOrDefault(i => i.EditorId == editorId);
        if (declaredEditor is not null)
        {
            return declaredEditor;
        }

        return _builtInEditors.FirstOrDefault(i => i.EditorId == editorId);
    }

    // Enumerates the contributions of the available editors: declared editors first (in
    // declaration order), then the built-ins.
    private IEnumerable<EditorContribution> GetAvailableContributions()
    {
        foreach (var resolvedEditor in _resolvedEditors)
        {
            yield return resolvedEditor.Contribution;
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

    // Publishes the per-extension icons declared by the catalog and by package manifests, so every
    // surface that draws a file resource picks them up through the icon service. The catalog wins for an
    // established type; a manifest icon covers the extensions a package introduces. An unusable glyph or
    // colour is dropped with a warning, leaving the extension on the bundled icon theme.
    private void ApplyFileIconOverrides(List<ContributionIssue> contributionIssues)
    {
        var overrides = new Dictionary<string, IconDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var contribution in GetAvailableContributions())
        {
            var editorId = EditorId.Create(contribution.Package.Name, contribution.Id).ToString();

            foreach (var fileType in contribution.FileTypes)
            {
                if (string.IsNullOrEmpty(fileType.Icon))
                {
                    continue;
                }

                var createResult = _iconService.CreateIcon(fileType.Icon, fileType.IconColor);
                if (createResult.IsFailure)
                {
                    _logger.LogWarning(
                        createResult,
                        $"Ignoring the icon declared for '{fileType.FileExtension}' in {contribution.ManifestPath}");

                    contributionIssues.Add(new ContributionIssue
                    {
                        EditorId = editorId,
                        Kind = ContributionIssueKind.UnresolvedIcon,
                        Value = fileType.Icon
                    });
                    continue;
                }

                overrides[fileType.FileExtension] = createResult.Value;
            }

            // A utility's icon is drawn straight from its name by the rail and its docked tab, so it never
            // reaches the override map. Resolve it here purely to report an unusable name.
            var utilityIcon = contribution.UtilityDescriptor?.Icon ?? string.Empty;
            if (!string.IsNullOrEmpty(utilityIcon) &&
                _iconService.CreateIcon(utilityIcon, string.Empty).IsFailure)
            {
                _logger.LogWarning(
                    $"Ignoring the utility icon declared in {contribution.ManifestPath}: unknown icon name '{utilityIcon}'");

                contributionIssues.Add(new ContributionIssue
                {
                    EditorId = editorId,
                    Kind = ContributionIssueKind.UnresolvedIcon,
                    Value = utilityIcon
                });
            }
        }

        foreach (var extension in _fileTypeCatalog.IconExtensions)
        {
            var catalogIcon = _fileTypeCatalog.GetIcon(extension);
            if (catalogIcon is null)
            {
                continue;
            }

            var createResult = _iconService.CreateIcon(catalogIcon.IconName, catalogIcon.Color);
            if (createResult.IsFailure)
            {
                _logger.LogWarning(createResult, $"Ignoring the file type catalog icon for '{extension}'");
                continue;
            }

            overrides[extension] = createResult.Value;
        }

        foreach (var publishedOverride in overrides)
        {
            var icon = publishedOverride.Value;
            var codePoint = icon.FontCharacter.Length > 0 ? (int)icon.FontCharacter[0] : 0;
            _logger.LogDebug(
                $"File icon override: '{publishedOverride.Key}' glyph U+{codePoint:X4} colour {icon.FontColor} font {icon.FontFamily}");
        }

        _iconService.SetFileIconOverrides(overrides);
    }

    // The project load report is a developer-facing artifact written in English, so a contribution issue
    // renders to plain text there rather than through the localized strings the UI uses.
    private static ResolvedEditorLoadFailure ToReportWarning(ContributionIssue issue)
    {
        var detail = issue.Kind switch
        {
            ContributionIssueKind.UnresolvedIcon => $"The icon '{issue.Value}' could not be resolved.",
            _ => $"The setting '{issue.Value}' could not be applied."
        };

        return new ResolvedEditorLoadFailure
        {
            EditorId = issue.EditorId,
            Detail = detail
        };
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
                reader: _bundledReader,
                fileTypeCatalog: _fileTypeCatalog);
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
                    $"Multiple bundled packages declare name '{group.Key}'. All {members.Count} packages skipped.");
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
                reader: projectReader,
                fileTypeCatalog: _fileTypeCatalog);
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
                    $"Multiple project packages declare name '{group.Key}'. All {members.Count} packages skipped to avoid ambiguity.");
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
