using Celbridge.Utilities;

namespace Celbridge.Packages.Helpers;

/// <summary>
/// Stands in for the host catalog when a caller supplies none, so a manifest that claims catalog
/// extensions loads with none rather than failing.
/// </summary>
internal sealed class EmptyFileTypeCatalog : IFileTypeCatalog
{
    public static readonly EmptyFileTypeCatalog Instance = new();

    public Task LoadAsync() => Task.CompletedTask;
    public bool IsBinaryExtension(string extension) => false;
    public string GetLanguage(string extension) => string.Empty;
    public string GetDisplayName(string extension) => string.Empty;
    public FileTypeIcon? GetIcon(string extension) => null;
    public FileTypeIcon? GetIconForFileName(string fileName) => null;
    public IReadOnlyList<string> LanguageExtensions => Array.Empty<string>();
    public IReadOnlyList<string> IconExtensions => Array.Empty<string>();
    public IReadOnlyList<string> IconFileNames => Array.Empty<string>();
}

/// <summary>
/// Parses a package.toml manifest into a Package: the [package] identity and permissions, plus the
/// list of editor contributions, each loaded from its referenced *.editor.toml by EditorManifestLoader.
/// </summary>
public static class PackageManifestLoader
{
    private const string PackageSection = "package";
    private const string ContributesSection = "contributes";
    private const string PermissionsSection = "permissions";

    private const string NameKey = "name";

    private static readonly IReadOnlyDictionary<string, string> EmptySecrets = new Dictionary<string, string>();

    /// <summary>
    /// Loads a package from a package.toml file, including all referenced editor contributions.
    /// secrets populates PackageInfo.Secrets for WebView injection.
    /// devToolsBlocked permanently disables DevTools on the package's WebViews.
    /// origin tags PackageInfo so downstream read sites pick the right IO path.
    /// reader is the file-read primitive. Null selects DirectPackageReader (direct disk) for callers with no
    /// IResourceFileSystem to route through, such as tests and bundled discovery.
    /// </summary>
    public static Result<Package> LoadPackage(
        string packageTomlPath,
        IReadOnlyDictionary<string, string>? secrets = null,
        bool devToolsBlocked = false,
        PackageOrigin origin = PackageOrigin.Bundled,
        IPackageReader? reader = null,
        IFileTypeCatalog? fileTypeCatalog = null)
    {
        reader ??= new DirectPackageReader();
        fileTypeCatalog ??= EmptyFileTypeCatalog.Instance;
        try
        {
            var packageFolder = Path.GetFullPath(Path.GetDirectoryName(packageTomlPath) ?? string.Empty);
            var readResult = reader.ReadAllText(packageTomlPath);
            if (readResult.IsFailure)
            {
                return Result.Fail($"Failed to read package manifest: {packageTomlPath}")
                    .WithErrors(readResult);
            }
            var toml = readResult.Value;

            var manifestResult = ManifestDeserializer.Deserialize<PackageManifest>(toml, packageTomlPath);
            if (manifestResult.IsFailure)
            {
                return Result<Package>.Fail(manifestResult.FirstErrorMessage)
                    .WithErrors(manifestResult);
            }
            var manifest = manifestResult.Value;

            var packageSection = manifest.Package;
            if (packageSection is null)
            {
                return Result.Fail($"Missing [{PackageSection}] section: {packageTomlPath}");
            }

            var packageName = packageSection.Name;
            if (string.IsNullOrEmpty(packageName))
            {
                return Result.Fail($"Package missing required '{NameKey}' field: {packageTomlPath}");
            }

            // One name grammar for every origin. The reserved "celbridge-" prefix is what marks a
            // bundled package, so a project package cannot impersonate one.
            if (!PackageName.IsValid(packageName))
            {
                return Result.Fail($"Package has invalid '{NameKey}' value '{packageName}': {packageTomlPath}. Package names must be lowercase ASCII letters and digits with single interior hyphens, at most {PackageConstants.MaxNameLength} characters.");
            }

            var permittedTools = Array.Empty<string>() as IReadOnlyList<string>;
            if (manifest.Permissions?.Tools is not null)
            {
                permittedTools = manifest.Permissions.Tools.AsReadOnly();
            }

            var packageSecrets = secrets ?? EmptySecrets;

            // The installed version is recorded in the generated HISTORY.md changelog beside the manifest.
            // Only project packages carry one; a hand-authored or absent file leaves the version unknown.
            int? packageVersion = null;
            if (origin == PackageOrigin.Project)
            {
                var historyPath = Path.Combine(packageFolder, PackageConstants.HistoryFileName);
                var historyResult = reader.ReadAllText(historyPath);
                if (historyResult.IsSuccess)
                {
                    packageVersion = PackageHistoryReader.TryReadInstalledVersion(historyResult.Value);
                }
            }

            var packageInfo = new PackageInfo
            {
                Name = packageName,
                Title = packageSection.Title ?? string.Empty,
                PackageFolder = packageFolder,
                PermittedTools = permittedTools,
                Secrets = packageSecrets,
                DevToolsBlocked = devToolsBlocked,
                Origin = origin,
                Version = packageVersion
            };

            var editorsResult = LoadEditors(manifest, packageInfo, packageTomlPath, packageFolder, reader, fileTypeCatalog);
            if (editorsResult.IsFailure)
            {
                return Result<Package>.Fail(editorsResult.FirstErrorMessage)
                    .WithErrors(editorsResult);
            }
            var editors = editorsResult.Value;

            var package = new Package
            {
                Info = packageInfo,
                Editors = editors.AsReadOnly(),
                UnknownFields = CollectUnknownFields(manifest)
            };

            return Result<Package>.Ok(package);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to load package: {packageTomlPath}").WithException(ex);
        }
    }

    private static Result<List<EditorContribution>> LoadEditors(
        PackageManifest manifest,
        PackageInfo packageInfo,
        string packageTomlPath,
        string packageFolder,
        IPackageReader reader,
        IFileTypeCatalog fileTypeCatalog)
    {
        var editors = new List<EditorContribution>();

        var editorManifestPaths = manifest.Contributes?.Editors;
        if (editorManifestPaths is null)
        {
            return editors;
        }

        foreach (var relativePath in editorManifestPaths)
        {
            if (!relativePath.EndsWith(PackageConstants.EditorManifestExtension, StringComparison.Ordinal))
            {
                return Result.Fail(
                    $"Editor manifest reference '{relativePath}' must use the '{PackageConstants.EditorManifestExtension}' extension: {packageTomlPath}");
            }

            var fullPath = Path.Combine(packageFolder, relativePath);
            var loadResult = EditorManifestLoader.LoadEditor(fullPath, packageInfo, reader, fileTypeCatalog);
            if (loadResult.IsFailure)
            {
                // The reason is folded into the message rather than nested, because the
                // package load failure reports only the first error message.
                return Result<List<EditorContribution>>.Fail(
                    $"Package '{packageInfo.Name}' has an invalid editor manifest '{relativePath}': {loadResult.FirstErrorMessage}")
                    .WithErrors(loadResult);
            }
            var contribution = loadResult.Value;

            if (editors.Any(e => string.Equals(e.Id, contribution.Id, StringComparison.Ordinal)))
            {
                return Result.Fail(
                    $"Package '{packageInfo.Name}' declares more than one editor with id '{contribution.Id}': {packageTomlPath}");
            }

            editors.Add(contribution);
        }

        return editors;
    }

    // Every field the manifest declares that the host does not define, named by its section.
    private static IReadOnlyList<string> CollectUnknownFields(PackageManifest manifest)
    {
        var unknownFields = new List<string>();

        unknownFields.AddRange(manifest.UnknownKeys.Keys);
        AddUnknownKeys(manifest.Package?.UnknownKeys, PackageSection, unknownFields);
        AddUnknownKeys(manifest.Contributes?.UnknownKeys, ContributesSection, unknownFields);
        AddUnknownKeys(manifest.Permissions?.UnknownKeys, PermissionsSection, unknownFields);

        return unknownFields.AsReadOnly();
    }

    private static void AddUnknownKeys(
        Dictionary<string, object?>? unknownKeys,
        string sectionName,
        List<string> unknownFields)
    {
        if (unknownKeys is null)
        {
            return;
        }

        foreach (var key in unknownKeys.Keys)
        {
            unknownFields.Add($"{sectionName}.{key}");
        }
    }
}
