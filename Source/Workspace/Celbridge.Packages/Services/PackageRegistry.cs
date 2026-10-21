using Celbridge.Documents;
using Celbridge.Logging;
using Celbridge.Packages.Helpers;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.UserInterface;
using Celbridge.Workspace;

namespace Celbridge.Packages;

/// <summary>
/// A package folder, with the manifest of each package that discovery found in it. IsPackage is true when the
/// folder itself contains a manifest.
/// </summary>
internal sealed record PackageFolderManifests(ResourceKey PackageFolder, bool IsPackage, IReadOnlyList<ResourceKey> Manifests);

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

    // The project file table that lists the package folders, named in the load report.
    private const string PackagesTableName = "celbridge.packages";

    private readonly ILogger<PackageRegistry> _logger;
    private readonly IReadOnlyList<IBundledPackageProvider> _bundledPackageProviders;
    private readonly IPackageLocalizationService _localizationService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectService _projectService;

    private List<Package> _bundledPackages = [];
    private List<Package> _projectPackages = [];
    // Each discovery assigns a new list, so a list returned to a caller never changes.
    private IReadOnlyList<ResourceKey> _packageFolders = [];

    private List<ResolvedEditor> _resolvedEditors = [];
    private List<ResolvedEditor> _builtInEditors = [];

    // The reconciled, normalized config from the discovery pass at project load, which writes it back to
    // the project file when discovery is clean.
    private ProjectConfig? _normalizedConfig;

    // Failures from the discovery pass at project load, retained so app_list_packages and app_get_state can
    // report them for the rest of the session.
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
        IEnumerable<IBundledPackageProvider> bundledPackageProviders,
        IPackageLocalizationService localizationService,
        IWorkspaceWrapper workspaceWrapper,
        IProjectService projectService,
        ILocalFileSystem fileSystem,
        IFileTypeCatalog fileTypeCatalog,
        IIconService iconService)
    {
        _logger = logger;
        _bundledPackageProviders = bundledPackageProviders.ToList();
        _localizationService = localizationService;
        _workspaceWrapper = workspaceWrapper;
        _projectService = projectService;
        _fileTypeCatalog = fileTypeCatalog;
        _iconService = iconService;
        _bundledReader = new DirectPackageReader(fileSystem);
    }

    public async Task<PackageDiscoveryReport> DiscoverPackagesAsync(string projectFolderPath)
    {
        _bundledPackages.Clear();
        _projectPackages.Clear();
        _packageFolders = [];

        // Manifests that claim their extensions from the catalog need it populated before they load.
        await _fileTypeCatalog.LoadAsync();

        var configEntryErrors = new List<ProjectConfigEntryError>();
        var bundledFailures = DiscoverBundledPackages();
        var projectFailures = await DiscoverProjectPackagesAsync(projectFolderPath, configEntryErrors);

        var failures = new List<PackageLoadFailure>(bundledFailures.Count + projectFailures.Count);
        failures.AddRange(bundledFailures);
        failures.AddRange(projectFailures);

        var resolvedEditorFailures = new List<ResolvedEditorLoadFailure>();
        var resolvedEditorWarnings = new List<ResolvedEditorLoadFailure>();
        var persistNormalizedConfig = failures.Count == 0;
        await ResolveResolvedEditorsAsync(persistNormalizedConfig, resolvedEditorFailures, resolvedEditorWarnings);
        ResolveBuiltInEditors();

        var contributionIssues = new List<ContributionIssue>();
        ReportUnknownManifestFields(contributionIssues);
        ApplyFileIconOverrides(contributionIssues);
        _lastContributionIssues = contributionIssues.AsReadOnly();

        resolvedEditorWarnings.AddRange(contributionIssues.Select(ToReportWarning));

        var report = new PackageDiscoveryReport
        {
            BundledPackageCount = _bundledPackages.Count,
            ProjectPackageCount = _projectPackages.Count,
            Failures = failures.AsReadOnly(),
            ConfigEntryErrors = configEntryErrors.AsReadOnly(),
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

    public IReadOnlyList<ResourceKey> GetPackageFolders()
    {
        return _packageFolders;
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
    // A manifest key the host does not define is advisory: the package still loads, and the field is
    // named so a package author can find it in the log.
    private void LogUnknownManifestFields(Package package, string manifestPath)
    {
        if (package.UnknownFields.Count > 0)
        {
            _logger.LogWarning(
                $"Package manifest declares fields the host does not define ({string.Join(", ", package.UnknownFields)}): {manifestPath}");
        }

        foreach (var contribution in package.Editors)
        {
            if (contribution.UnknownFields.Count > 0)
            {
                _logger.LogWarning(
                    $"Editor manifest declares fields the host does not define ({string.Join(", ", contribution.UnknownFields)}): {contribution.ManifestPath}");
            }
        }
    }

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
    // workspace-scoped IResourceFileSystem and IRootHandlerRegistry must be looked
    // up at call time.
    private IPackageReader GetReaderForPackage(PackageInfo package)
    {
        if (package.Origin == PackageOrigin.Project)
        {
            var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
            var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlers;
            return new ResourceFileSystemPackageReader(resourceFileSystem, rootHandlerRegistry);
        }

        return _bundledReader;
    }

    private void ReportUnknownManifestFields(List<ContributionIssue> contributionIssues)
    {
        foreach (var contribution in GetAvailableContributions())
        {
            if (contribution.UnknownFields.Count == 0)
            {
                continue;
            }

            var editorId = EditorId.Create(contribution.Package.Name, contribution.Id).ToString();

            foreach (var unknownField in contribution.UnknownFields)
            {
                _logger.LogWarning(
                    $"Ignoring the unknown field '{unknownField}' declared in {contribution.ManifestPath}");

                contributionIssues.Add(new ContributionIssue
                {
                    EditorId = editorId,
                    Kind = ContributionIssueKind.UnknownField,
                    Value = unknownField
                });
            }
        }
    }

    // Publishes the per-extension icons declared by the catalog and by package manifests, so every
    // surface that draws a file resource picks them up through the icon service. The catalog wins for an
    // established type. A manifest icon covers the extensions a package introduces. An unusable glyph or
    // colour is dropped with a warning, leaving the extension on the default file icon.
    private void ApplyFileIconOverrides(List<ContributionIssue> contributionIssues)
    {
        var overrides = new Dictionary<string, IconDefinition>(StringComparer.OrdinalIgnoreCase);
        var fileNameOverrides = new Dictionary<string, IconDefinition>(StringComparer.OrdinalIgnoreCase);

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

                overrides[fileType.FileExtension] = ApplyScale(createResult.Value, fileType.IconScale);
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

            overrides[extension] = ApplyScale(createResult.Value, catalogIcon.Scale);
        }

        foreach (var fileName in _fileTypeCatalog.IconFileNames)
        {
            var catalogIcon = _fileTypeCatalog.GetIconForFileName(fileName);
            if (catalogIcon is null)
            {
                continue;
            }

            var createResult = _iconService.CreateIcon(catalogIcon.IconName, catalogIcon.Color);
            if (createResult.IsFailure)
            {
                _logger.LogWarning(createResult, $"Ignoring the file type catalog icon for '{fileName}'");
                continue;
            }

            fileNameOverrides[fileName] = ApplyScale(createResult.Value, catalogIcon.Scale);
        }

        _logger.LogDebug(
            $"Registered {overrides.Count} file extension and {fileNameOverrides.Count} file name icon overrides");

        _iconService.SetFileIconOverrides(overrides, fileNameOverrides);
    }

    // The catalog scale is drawn relative to the host's icon size, so it is carried on the definition as a
    // percentage the FileIcon control multiplies its size by.
    private static IconDefinition ApplyScale(IconDefinition icon, double scale)
    {
        if (scale == 1.0)
        {
            return icon;
        }

        var percent = (int)Math.Round(scale * 100.0);

        return icon with { FontSize = $"{percent}%" };
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

        // Every bundled package is contributed by an IBundledPackageProvider, whether from a module or a
        // core project like the console. Discovered on every workspace load. Descriptors without a folder
        // are ignored.
        var descriptors = new List<BundledPackageDescriptor>();
        foreach (var provider in _bundledPackageProviders)
        {
            foreach (var descriptor in provider.GetBundledPackages())
            {
                if (!string.IsNullOrEmpty(descriptor.Folder))
                {
                    descriptors.Add(descriptor);
                }
            }
        }

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
                    Origin = PackageOrigin.Bundled,
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
                    Origin = PackageOrigin.Bundled,
                    PackageName = null,
                    Reason = PackageLoadFailureReason.InvalidManifest,
                    Detail = loadResult.FirstErrorMessage
                });
                continue;
            }

            var package = loadResult.Value;
            LogUnknownManifestFields(package, manifestPath);

            var reservedExtensionCheck = CheckReservedExtensions(package);
            if (reservedExtensionCheck.IsFailure)
            {
                _logger.LogError(reservedExtensionCheck, $"Bundled package uses reserved extension: {manifestPath}");
                failures.Add(new PackageLoadFailure
                {
                    Folder = descriptor.Folder,
                    Origin = PackageOrigin.Bundled,
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
                        Origin = PackageOrigin.Bundled,
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

    private async Task<List<PackageLoadFailure>> DiscoverProjectPackagesAsync(
        string projectFolderPath,
        List<ProjectConfigEntryError> configEntryErrors)
    {
        var failures = new List<PackageLoadFailure>();

        if (string.IsNullOrEmpty(projectFolderPath))
        {
            return failures;
        }

        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
        var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlers;
        var projectReader = new ResourceFileSystemPackageReader(resourceFileSystem, rootHandlerRegistry);

        var manifestResources = await CollectProjectManifestsAsync(resourceFileSystem, rootHandlerRegistry, configEntryErrors);

        var candidates = new List<Package>();

        foreach (var manifestResource in manifestResources)
        {
            var resolveResult = rootHandlerRegistry.ResolveResourcePath(manifestResource);
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
                    Origin = PackageOrigin.Project,
                    PackageName = null,
                    Reason = PackageLoadFailureReason.InvalidManifest,
                    Detail = loadResult.FirstErrorMessage
                });
                continue;
            }

            var package = loadResult.Value;
            LogUnknownManifestFields(package, manifestPath);

            var reservedExtensionCheck = CheckReservedExtensions(package);
            if (reservedExtensionCheck.IsFailure)
            {
                _logger.LogWarning(reservedExtensionCheck, $"Skipping project package using reserved extension: {manifestPath}");
                failures.Add(new PackageLoadFailure
                {
                    Folder = packageFolder,
                    Origin = PackageOrigin.Project,
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
                    Origin = PackageOrigin.Project,
                    PackageName = package.Info.Name,
                    Reason = PackageLoadFailureReason.ReservedNamePrefix
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
                    Origin = PackageOrigin.Project,
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
                        Origin = PackageOrigin.Project,
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

    // Collects the manifests of the packages in every package folder: the packages folder first, then the
    // folders that the project file lists, in their order. Each package folder is listed, and nothing else is
    // walked. Discovery refuses a listed folder that is a package, that is a package of another package folder,
    // or that lies inside such a package. Loading that folder would nest one package inside another.
    // GetPackageFolders returns the folders that discovery accepts.
    private async Task<List<ResourceKey>> CollectProjectManifestsAsync(
        IResourceFileSystem resourceFileSystem,
        IRootHandlerRegistry rootHandlerRegistry,
        List<ProjectConfigEntryError> configEntryErrors)
    {
        var defaultFolder = PackageFolderPath.DefaultFolder;
        var candidateFolders = new List<ResourceKey>
        {
            defaultFolder
        };

        var listedFolders = _projectService.CurrentProject?.Config.Packages.Folders ?? [];
        foreach (var listedFolder in PackageFolderPath.NormalizeListedFolders(listedFolders))
        {
            candidateFolders.Add(new ResourceKey(listedFolder));
        }

        // Every folder is listed before any is accepted, so each listed folder is checked against the
        // packages of every other folder. A folder key that does not match the folder's stored name is refused,
        // since every key built inside the folder would be refused too.
        var packageManifests = new List<PackageFolderManifests>();
        foreach (var candidateFolder in candidateFolders)
        {
            var resolveResult = rootHandlerRegistry.ResolveResourcePath(candidateFolder);
            if (resolveResult.FirstException is StoredNameMismatchError mismatch)
            {
                RefuseFolderNameMismatch(configEntryErrors, candidateFolder, defaultFolder, mismatch.StoredResource);
                continue;
            }

            packageManifests.Add(await ListPackageFolderAsync(resourceFileSystem, candidateFolder));
        }

        var acceptedFolders = new List<ResourceKey>();
        var manifestResources = new List<ResourceKey>();
        foreach (var folderManifests in packageManifests)
        {
            var packageFolder = folderManifests.PackageFolder;

            var containingPackage = FindContainingPackage(packageFolder, packageManifests);
            if (containingPackage is ResourceKey package)
            {
                RefuseListedFolder(
                    configEntryErrors,
                    $"'folders' entry '{packageFolder.Path}' is inside the package at '{package.Path}', and packages cannot nest. The entry was dropped.");
                continue;
            }

            // A manifest in the packages folder itself is an ordinary file.
            if (folderManifests.IsPackage
                && packageFolder != defaultFolder)
            {
                RefuseListedFolder(
                    configEntryErrors,
                    $"'folders' entry '{packageFolder.Path}' is a package, since it has a {PackageConstants.ManifestFileName}. List the folder that contains the package instead. The entry was dropped.");
                continue;
            }

            acceptedFolders.Add(packageFolder);
            manifestResources.AddRange(folderManifests.Manifests);
        }

        _packageFolders = acceptedFolders.AsReadOnly();

        return manifestResources;
    }

    private void RefuseFolderNameMismatch(
        List<ProjectConfigEntryError> configEntryErrors,
        ResourceKey packageFolder,
        ResourceKey defaultFolder,
        ResourceKey? storedFolder)
    {
        var storedName = storedFolder is ResourceKey stored
            ? $", '{stored.Path}'"
            : string.Empty;

        if (packageFolder == defaultFolder)
        {
            RefuseListedFolder(
                configEntryErrors,
                $"The '{defaultFolder.Path}' folder does not match its name on disk{storedName}, so its packages were not loaded. Rename the folder to '{defaultFolder.Path}' to load them.");
            return;
        }

        RefuseListedFolder(
            configEntryErrors,
            $"'folders' entry '{packageFolder.Path}' does not match the folder's name on disk{storedName}. Change the entry to match the name. The entry was dropped.");
    }

    private void RefuseListedFolder(List<ProjectConfigEntryError> configEntryErrors, string message)
    {
        _logger.LogWarning(message);
        configEntryErrors.Add(new ProjectConfigEntryError(PackagesTableName, message));
    }

    // Lists a package folder and the manifest of each subfolder that has one. A manifest matches in any letter
    // case. A missing package folder contributes nothing.
    private static async Task<PackageFolderManifests> ListPackageFolderAsync(
        IResourceFileSystem resourceFileSystem,
        ResourceKey packageFolder)
    {
        var enumerateResult = await resourceFileSystem.EnumerateFolderAsync(packageFolder);
        if (enumerateResult.IsFailure)
        {
            return new PackageFolderManifests(packageFolder, IsPackage: false, Manifests: []);
        }

        var items = enumerateResult.Value;

        var manifests = new List<ResourceKey>();
        foreach (var item in items)
        {
            if (!item.IsFolder)
            {
                continue;
            }

            var childResult = await resourceFileSystem.EnumerateFolderAsync(item.Resource);
            if (childResult.IsFailure)
            {
                continue;
            }

            var manifest = FindManifest(childResult.Value);
            if (manifest is not null)
            {
                manifests.Add(manifest.Resource);
            }
        }

        var isPackage = FindManifest(items) is not null;

        return new PackageFolderManifests(packageFolder, isPackage, manifests);
    }

    private static FolderItem? FindManifest(IReadOnlyList<FolderItem> items)
    {
        return items.FirstOrDefault(item =>
            !item.IsFolder
            && string.Equals(item.Resource.ResourceName, PackageConstants.ManifestFileName, StringComparison.OrdinalIgnoreCase));
    }

    // Finds a package from another package folder that equals the folder or contains it, and returns the
    // package's folder.
    private static ResourceKey? FindContainingPackage(
        ResourceKey folder,
        IReadOnlyList<PackageFolderManifests> packageManifests)
    {
        foreach (var otherFolderManifests in packageManifests)
        {
            if (otherFolderManifests.PackageFolder == folder)
            {
                continue;
            }

            foreach (var manifest in otherFolderManifests.Manifests)
            {
                var package = manifest.GetParent();
                if (IsWithin(folder, package))
                {
                    return package;
                }
            }
        }

        return null;
    }

    // Returns true if the folder is the parent folder or anything below it.
    private static bool IsWithin(ResourceKey folder, ResourceKey parentFolder)
    {
        if (PackageFolderPath.IsSameFolder(folder.Path, parentFolder.Path))
        {
            return true;
        }

        return folder.Path.StartsWith(parentFolder.Path + "/", PathComparison.Comparison);
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
