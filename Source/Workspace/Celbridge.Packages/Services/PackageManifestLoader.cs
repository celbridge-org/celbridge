using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;

namespace Celbridge.Packages;

/// <summary>
/// Parses a package.toml manifest into a Package: the [package] identity and permissions, plus the
/// list of editor contributions, each loaded from its referenced *.editor.toml by EditorManifestLoader.
/// </summary>
public static class PackageManifestLoader
{
    private const string PackageSection = "package";
    private const string ContributesSection = "contributes";
    private const string PermissionsSection = "permissions";
    private const string EditorsKey = "editors";
    private const string ToolsKey = "tools";

    private const string NameKey = "name";
    private const string TitleKey = "title";

    /// <summary>
    /// File extension of an editor manifest.
    /// </summary>
    public const string EditorManifestExtension = ".editor.toml";

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
        IPackageReader? reader = null)
    {
        reader ??= new DirectPackageReader();
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
            var parsed = SyntaxParser.Parse(toml);

            if (parsed.HasErrors)
            {
                var errors = string.Join("; ", parsed.Diagnostics.Select(d => d.ToString()));
                return Result.Fail($"TOML parse error in {packageTomlPath}: {errors}");
            }

            var root = TomlSerializer.Deserialize<TomlTable>(toml);
            if (root is null)
            {
                return Result.Fail($"Failed to deserialize package manifest: {packageTomlPath}");
            }

            if (!root.TryGetValue(PackageSection, out var packageObject) ||
                packageObject is not TomlTable packageTable)
            {
                return Result.Fail($"Missing [{PackageSection}] section: {packageTomlPath}");
            }

            var packageName = TomlTableReader.GetString(packageTable, NameKey);
            if (string.IsNullOrEmpty(packageName))
            {
                return Result.Fail($"Package missing required '{NameKey}' field: {packageTomlPath}");
            }

            // Bundled first-party packages use dotted names (e.g. "celbridge.notes"),
            // so structural validation accepts the dotted form for every origin.
            // Project discovery rejects dotted names downstream with a specific
            // failure reason.
            if (!PackageName.IsValidBundledName(packageName))
            {
                return Result.Fail($"Package has invalid '{NameKey}' value '{packageName}': {packageTomlPath}. Package names must be lowercase ASCII letters and digits with single interior hyphens, at most {PackageConstants.MaxNameLength} characters.");
            }

            var packageTitle = TomlTableReader.GetString(packageTable, TitleKey);

            var permittedTools = Array.Empty<string>() as IReadOnlyList<string>;
            if (root.TryGetValue(PermissionsSection, out var permissionsObject) &&
                permissionsObject is TomlTable permissionsTable)
            {
                permittedTools = TomlTableReader.GetStringArray(permissionsTable, ToolsKey);
            }

            var packageSecrets = secrets ?? EmptySecrets;

            var packageInfo = new PackageInfo
            {
                Name = packageName,
                Title = packageTitle,
                PackageFolder = packageFolder,
                PermittedTools = permittedTools,
                Secrets = packageSecrets,
                DevToolsBlocked = devToolsBlocked,
                Origin = origin
            };

            var editorManifestPaths = new List<string>();
            if (root.TryGetValue(ContributesSection, out var contributesObject) &&
                contributesObject is TomlTable contributesTable)
            {
                if (contributesTable.TryGetValue(EditorsKey, out var editorsObject))
                {
                    if (editorsObject is not TomlArray editorsArray)
                    {
                        return Result<Package>.Fail(
                            $"'{ContributesSection}.{EditorsKey}' must be an array of editor manifest paths: {packageTomlPath}");
                    }

                    foreach (var editorEntry in editorsArray)
                    {
                        if (editorEntry is not string editorManifestPath)
                        {
                            return Result<Package>.Fail(
                                $"'{ContributesSection}.{EditorsKey}' entries must be strings: {packageTomlPath}");
                        }

                        editorManifestPaths.Add(editorManifestPath);
                    }
                }
            }

            var editors = new List<EditorContribution>();
            foreach (var relativePath in editorManifestPaths)
            {
                if (!relativePath.EndsWith(EditorManifestExtension, StringComparison.Ordinal))
                {
                    return Result.Fail(
                        $"Editor manifest reference '{relativePath}' must use the '{EditorManifestExtension}' extension: {packageTomlPath}");
                }

                var fullPath = Path.Combine(packageFolder, relativePath);
                var loadResult = EditorManifestLoader.LoadEditor(fullPath, packageInfo, reader);
                if (loadResult.IsFailure)
                {
                    // The reason is folded into the message rather than nested, because the
                    // package load failure reports only the first error message.
                    return Result<Package>.Fail(
                        $"Package '{packageName}' has an invalid editor manifest '{relativePath}': {loadResult.FirstErrorMessage}")
                        .WithErrors(loadResult);
                }
                var contribution = loadResult.Value;

                if (editors.Any(e => string.Equals(e.Id, contribution.Id, StringComparison.Ordinal)))
                {
                    return Result.Fail(
                        $"Package '{packageName}' declares more than one editor with id '{contribution.Id}': {packageTomlPath}");
                }

                editors.Add(contribution);
            }

            var package = new Package
            {
                Info = packageInfo,
                Editors = editors.AsReadOnly()
            };

            return Result<Package>.Ok(package);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to load package: {packageTomlPath}").WithException(ex);
        }
    }
}
