using Celbridge.Utilities;
using Celbridge.Workspace;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;

namespace Celbridge.Projects;

/// <summary>
/// Static utility class for parsing Celbridge project configuration files (v2 schema).
/// Host-level declarations live on the [celbridge] table. Every other top-level table declares
/// an editor contribution. Malformed entries are skipped with a recorded entry error. A TOML syntax
/// error fails the whole parse.
/// </summary>
public static class ProjectConfigParser
{
    private const string CelbridgeSectionName = "celbridge";
    private const string ContributionSectionName = "contribution";
    private const string DocumentShortcutSectionName = "shortcut";

    private const string CelbridgeVersionKey = "celbridge-version";
    private const string ProjectVersionKey = "project-version";
    private const string DescriptionKey = "description";
    private const string EditorAssociationsKey = "editor-associations";
    private const string FeaturesKey = "features";
    private const string ResourcesKey = "resources";
    private const string PackagesKey = "packages";

    private const string HideKey = "hide";
    private const string SearchExcludeKey = "search-exclude";
    private const string DownloadsFolderKey = "downloads-folder";

    private const string PackageFoldersKey = "folders";
    private const string DisabledPackagesKey = "disabled";

    private const string DocumentShortcutResourceKey = "resource";
    private const string DocumentShortcutIconKey = "icon";
    private const string DocumentShortcutAreaKey = "area";
    private const string DocumentShortcutRailButtonKey = "rail-button";
    private const string DocumentShortcutOpenOnLoadKey = "open-on-load";

    // The areas a shortcut can open into, spelled out for the error message it produces. The
    // Utility Panel is absent because it holds no document tabs.
    private const string ValidDocumentShortcutAreaTokens =
        $"{WorkspaceAreaTokens.Main}, {WorkspaceAreaTokens.Bottom}, {WorkspaceAreaTokens.Side}";

    private static readonly IReadOnlySet<string> KnownCelbridgeKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        CelbridgeVersionKey,
        ProjectVersionKey,
        DescriptionKey,
        EditorAssociationsKey,
        FeaturesKey,
        ResourcesKey,
        PackagesKey,
    };

    private static readonly IReadOnlySet<string> KnownResourcesKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        HideKey,
        SearchExcludeKey,
        DownloadsFolderKey,
    };

    private static readonly IReadOnlySet<string> KnownPackagesKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        PackageFoldersKey,
        DisabledPackagesKey,
    };

    private static readonly IReadOnlySet<string> KnownDocumentShortcutKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        DocumentShortcutResourceKey,
        DocumentShortcutIconKey,
        DocumentShortcutAreaKey,
        DocumentShortcutRailButtonKey,
        DocumentShortcutOpenOnLoadKey,
    };

    /// <summary>
    /// Parses a project config from a .celbridge file.
    /// Returns an empty config if the file doesn't exist.
    /// </summary>
    public static Result<ProjectConfig> ParseFromFile(string configFilePath)
    {
        // Static class cannot receive DI, so fall back to the service locator
        // to acquire the file system gateway.
        var fileSystem = ServiceLocator.AcquireService<ILocalFileSystem>();
        return ParseFromFile(configFilePath, fileSystem);
    }

    /// <summary>
    /// Parses a project config from a .celbridge file using the supplied file system.
    /// Returns an empty config if the file doesn't exist.
    /// </summary>
    public static Result<ProjectConfig> ParseFromFile(string configFilePath, ILocalFileSystem fileSystem)
    {
        try
        {
            var infoResult = SyncRunner.Run(() => fileSystem.GetInfoAsync(configFilePath));
            if (infoResult.IsFailure || infoResult.Value.Kind != StorageItemKind.File)
            {
                return Result<ProjectConfig>.Ok(new ProjectConfig());
            }

            var readResult = SyncRunner.Run(() => fileSystem.ReadAllTextAsync(configFilePath));
            if (readResult.IsFailure)
            {
                return Result<ProjectConfig>.Fail($"Failed to read TOML file: {configFilePath}")
                    .WithErrors(readResult);
            }

            return ParseFromText(readResult.Value);
        }
        catch (Exception ex)
        {
            return Result<ProjectConfig>.Fail($"Failed to read TOML file: {configFilePath}")
                .WithException(ex);
        }
    }

    /// <summary>
    /// Parses a project config from TOML text. A syntax error fails the whole parse.
    /// </summary>
    public static Result<ProjectConfig> ParseFromText(string tomlText)
    {
        // Tomlyn rejects bare-\r line terminators. Normalize once here so
        // a project config written with non-standard line endings still
        // parses cleanly.
        var text = LineEndingHelper.ConvertLineEndings(tomlText, "\n");
        var parse = SyntaxParser.Parse(text);
        if (parse.HasErrors)
        {
            var errors = string.Join("; ", parse.Diagnostics.Select(d => d.ToString()));
            return Result<ProjectConfig>.Fail($"TOML parse error(s): {errors}");
        }

        var root = TomlSerializer.Deserialize<TomlTable>(text);
        if (root is null)
        {
            return Result<ProjectConfig>.Fail("Failed to deserialize TOML text.");
        }

        var config = MapRootToModel(root);

        return Result<ProjectConfig>.Ok(config);
    }

    private static ProjectConfig MapRootToModel(TomlTable root)
    {
        var entryErrors = new List<ProjectConfigEntryError>();

        var celbridgeSection = new CelbridgeSection();
        var resourcesSection = new ResourcesSection();
        var packagesSection = new PackagesSection();
        var featuresDict = new Dictionary<string, bool>();

        if (root.TryGetValue(CelbridgeSectionName, out var celbridgeObject) &&
            celbridgeObject is TomlTable celbridgeTable)
        {
            celbridgeSection = ParseCelbridgeTable(celbridgeTable, entryErrors, out featuresDict);
            resourcesSection = ParseResourcesTable(celbridgeTable, entryErrors);
            packagesSection = ParsePackagesTable(celbridgeTable, entryErrors);
        }

        // Editor overrides of the discovered defaults are declared as [[contribution]] entries.
        var contributions = new List<ContributionOverride>();
        if (root.TryGetValue(ContributionSectionName, out var contributionObject))
        {
            if (contributionObject is TomlTableArray contributionArray)
            {
                for (int i = 0; i < contributionArray.Count; i++)
                {
                    var contributionOverride = ParseContributionEntry(contributionArray[i], i + 1, entryErrors);
                    if (contributionOverride is not null)
                    {
                        contributions.Add(contributionOverride);
                    }
                }
            }
            else
            {
                entryErrors.Add(new ProjectConfigEntryError(
                    ContributionSectionName,
                    $"'{ContributionSectionName}' must be declared as [[{ContributionSectionName}]] entries. The section was dropped."));
            }
        }

        // Document shortcuts are declared as [[shortcut]] entries. Entry order is rail order, so the
        // entries are kept in the order the file lists them.
        var documentShortcuts = new List<DocumentShortcut>();
        if (root.TryGetValue(DocumentShortcutSectionName, out var documentShortcutObject))
        {
            if (documentShortcutObject is TomlTableArray documentShortcutArray)
            {
                for (int i = 0; i < documentShortcutArray.Count; i++)
                {
                    var documentShortcut = ParseDocumentShortcutEntry(documentShortcutArray[i], i + 1, entryErrors);
                    if (documentShortcut is not null)
                    {
                        documentShortcuts.Add(documentShortcut);
                    }
                }
            }
            else
            {
                entryErrors.Add(new ProjectConfigEntryError(
                    DocumentShortcutSectionName,
                    $"'{DocumentShortcutSectionName}' must be declared as [[{DocumentShortcutSectionName}]] entries. The section was dropped."));
            }
        }

        // Any other top-level key is not part of the schema.
        foreach (var (key, _) in root)
        {
            if (key == CelbridgeSectionName ||
                key == ContributionSectionName ||
                key == DocumentShortcutSectionName)
            {
                continue;
            }

            if (key == FeaturesKey ||
                key == ResourcesKey)
            {
                entryErrors.Add(new ProjectConfigEntryError(
                    key, $"The top-level [{key}] section has moved to [{CelbridgeSectionName}]. The section was dropped."));
                continue;
            }

            entryErrors.Add(new ProjectConfigEntryError(
                key,
                $"Top-level key '{key}' is not allowed. Declare an editor override as a [[{ContributionSectionName}]] entry; host-level keys belong on the [{CelbridgeSectionName}] table."));
        }

        return new ProjectConfig
        {
            Celbridge = celbridgeSection,
            Resources = resourcesSection,
            Packages = packagesSection,
            Features = featuresDict,
            ContributionOverrides = contributions,
            DocumentShortcuts = documentShortcuts,
            EntryErrors = entryErrors
        };
    }

    private static CelbridgeSection ParseCelbridgeTable(
        TomlTable celbridgeTable,
        List<ProjectConfigEntryError> entryErrors,
        out Dictionary<string, bool> featuresDict)
    {
        featuresDict = new Dictionary<string, bool>();

        foreach (var key in ConfigSchemaHelper.FindUnknownKeys(celbridgeTable.Keys, KnownCelbridgeKeys))
        {
            entryErrors.Add(new ProjectConfigEntryError(
                CelbridgeSectionName, $"Unknown key '{key}' on [{CelbridgeSectionName}]. The key was dropped."));
        }

        var editorAssociations = new Dictionary<string, string>();
        if (celbridgeTable.TryGetValue(EditorAssociationsKey, out var editorAssociationsObject))
        {
            if (editorAssociationsObject is TomlTable editorAssociationsTable)
            {
                foreach (var (extension, editorObject) in editorAssociationsTable)
                {
                    if (editorObject is not string editorId || string.IsNullOrWhiteSpace(editorId))
                    {
                        entryErrors.Add(new ProjectConfigEntryError(
                            CelbridgeSectionName, $"'{EditorAssociationsKey}' entry '{extension}' must name an editor id. The entry was dropped."));
                        continue;
                    }

                    if (!FileExtensionUtils.IsWellFormedFileExtension(extension))
                    {
                        entryErrors.Add(new ProjectConfigEntryError(
                            CelbridgeSectionName, $"'{EditorAssociationsKey}' key '{extension}' must be a well-formed file extension (e.g. \".txt\"). The entry was dropped."));
                        continue;
                    }

                    editorAssociations[extension.ToLowerInvariant()] = editorId;
                }
            }
            else
            {
                entryErrors.Add(new ProjectConfigEntryError(
                    CelbridgeSectionName, $"'{EditorAssociationsKey}' must be an inline table mapping extensions to editor ids."));
            }
        }

        if (celbridgeTable.TryGetValue(FeaturesKey, out var featuresObject))
        {
            if (featuresObject is TomlTable featuresTable)
            {
                foreach (var (featureKey, featureValue) in featuresTable)
                {
                    if (featureValue is bool featureEnabled)
                    {
                        featuresDict[featureKey] = featureEnabled;
                    }
                    else
                    {
                        entryErrors.Add(new ProjectConfigEntryError(
                            CelbridgeSectionName, $"'{FeaturesKey}' entry '{featureKey}' must be a boolean. The entry was dropped."));
                    }
                }
            }
            else
            {
                entryErrors.Add(new ProjectConfigEntryError(
                    CelbridgeSectionName, $"'{FeaturesKey}' must be an inline table of feature flags."));
            }
        }

        return new CelbridgeSection
        {
            CelbridgeVersion = ReadString(celbridgeTable, CelbridgeVersionKey),
            ProjectVersion = ReadProjectVersion(celbridgeTable, entryErrors),
            Description = ReadString(celbridgeTable, DescriptionKey),
            EditorAssociations = editorAssociations
        };
    }

    private static ResourcesSection ParseResourcesTable(TomlTable celbridgeTable, List<ProjectConfigEntryError> entryErrors)
    {
        var resourcesSection = new ResourcesSection();

        var resourcesTable = ReadSubTable(celbridgeTable, ResourcesKey, KnownResourcesKeys, entryErrors);
        if (resourcesTable is null)
        {
            return resourcesSection;
        }

        return resourcesSection with
        {
            Hide = ReadStringList(resourcesTable, HideKey) ?? resourcesSection.Hide,
            SearchExclude = ReadStringList(resourcesTable, SearchExcludeKey) ?? resourcesSection.SearchExclude,
            DownloadsFolder = ReadDownloadsFolder(resourcesTable, entryErrors),
        };
    }

    private static PackagesSection ParsePackagesTable(TomlTable celbridgeTable, List<ProjectConfigEntryError> entryErrors)
    {
        var packagesTable = ReadSubTable(celbridgeTable, PackagesKey, KnownPackagesKeys, entryErrors);
        if (packagesTable is null)
        {
            return new PackagesSection();
        }

        return new PackagesSection
        {
            Folders = ReadPackageFolders(packagesTable, entryErrors),
            Disabled = ReadDisabledPackages(packagesTable, entryErrors),
        };
    }

    // Returns the [celbridge.<key>] sub-table, or null when the file has none or the key is not a table.
    private static TomlTable? ReadSubTable(
        TomlTable celbridgeTable,
        string key,
        IReadOnlySet<string> knownKeys,
        List<ProjectConfigEntryError> entryErrors)
    {
        if (!celbridgeTable.TryGetValue(key, out var subTableObject))
        {
            return null;
        }

        var tableName = $"{CelbridgeSectionName}.{key}";

        if (subTableObject is not TomlTable subTable)
        {
            entryErrors.Add(new ProjectConfigEntryError(
                CelbridgeSectionName, $"'{key}' must be a [{tableName}] table."));

            return null;
        }

        // A flat key hand-appended after the sub-table's header lands in the sub-table per TOML rules.
        // Unknown keys are reported, so the mistake shows in the load report.
        foreach (var unknownKey in ConfigSchemaHelper.FindUnknownKeys(subTable.Keys, knownKeys))
        {
            entryErrors.Add(new ProjectConfigEntryError(
                tableName,
                $"Unknown key '{unknownKey}' on [{tableName}]. Flat [{CelbridgeSectionName}] keys must precede the [{tableName}] header."));
        }

        return subTable;
    }

    // An entry that names the project root or is not a folder path is reported and dropped. The packages
    // folder and a repeated folder are dropped without a report, since listing them adds nothing.
    private static IReadOnlyList<string> ReadPackageFolders(TomlTable packagesTable, List<ProjectConfigEntryError> entryErrors)
    {
        var folders = new List<string>();
        if (!packagesTable.TryGetValue(PackageFoldersKey, out var foldersObject))
        {
            return folders;
        }

        var tableName = $"{CelbridgeSectionName}.{PackagesKey}";

        if (foldersObject is not TomlArray foldersArray)
        {
            entryErrors.Add(new ProjectConfigEntryError(
                tableName, $"'{PackageFoldersKey}' must be an array of folder paths."));

            return folders;
        }

        foreach (var entry in foldersArray)
        {
            if (entry is not string folderPath)
            {
                entryErrors.Add(new ProjectConfigEntryError(
                    tableName, $"Dropped a non-string entry in '{PackageFoldersKey}'."));
                continue;
            }

            if (PackageFolderPath.IsProjectRoot(folderPath))
            {
                entryErrors.Add(new ProjectConfigEntryError(
                    tableName,
                    $"'{PackageFoldersKey}' entry '{folderPath}' names the project root, which cannot be a package folder. The entry was dropped."));
                continue;
            }

            if (!PackageFolderPath.TryParse(folderPath, out var folder))
            {
                entryErrors.Add(new ProjectConfigEntryError(
                    tableName,
                    $"'{PackageFoldersKey}' entry '{folderPath}' must be a folder path from the project root. The entry was dropped."));
                continue;
            }

            if (PackageFolderPath.IsListed(folders, folder.Path))
            {
                continue;
            }

            folders.Add(folder.Path);
        }

        return folders;
    }

    private static IReadOnlyList<string> ReadDisabledPackages(TomlTable packagesTable, List<ProjectConfigEntryError> entryErrors)
    {
        var disabledPackages = new List<string>();
        if (!packagesTable.TryGetValue(DisabledPackagesKey, out var disabledPackagesObject))
        {
            return disabledPackages;
        }

        var tableName = $"{CelbridgeSectionName}.{PackagesKey}";

        if (disabledPackagesObject is not TomlArray disabledPackagesArray)
        {
            entryErrors.Add(new ProjectConfigEntryError(
                tableName, $"'{DisabledPackagesKey}' must be an array of package names."));

            return disabledPackages;
        }

        foreach (var entry in disabledPackagesArray)
        {
            if (entry is string packageName && !string.IsNullOrWhiteSpace(packageName))
            {
                disabledPackages.Add(packageName);
            }
            else
            {
                entryErrors.Add(new ProjectConfigEntryError(
                    tableName, $"Dropped a non-string entry in '{DisabledPackagesKey}'."));
            }
        }

        return disabledPackages;
    }

    // A malformed version is reported and dropped, which leaves the project at the default version.
    private static string? ReadProjectVersion(TomlTable celbridgeTable, List<ProjectConfigEntryError> entryErrors)
    {
        var projectVersion = ReadString(celbridgeTable, ProjectVersionKey);

        var parseResult = SemanticVersion.ParseOptional(projectVersion);
        if (parseResult.IsFailure)
        {
            entryErrors.Add(new ProjectConfigEntryError(
                CelbridgeSectionName,
                $"'{ProjectVersionKey}': {parseResult.FirstErrorMessage} The key was dropped."));

            return null;
        }

        return projectVersion;
    }

    // An unusable path is dropped rather than applied, which leaves downloads going to the default folder.
    private static string ReadDownloadsFolder(TomlTable resourcesTable, List<ProjectConfigEntryError> entryErrors)
    {
        var downloadsFolder = ReadString(resourcesTable, DownloadsFolderKey);
        if (string.IsNullOrEmpty(downloadsFolder))
        {
            return string.Empty;
        }

        if (!DownloadsFolderPath.TryParse(downloadsFolder, out var folder))
        {
            var problem = DownloadsFolderPath.IsReserved(downloadsFolder)
                ? "is inside a folder Celbridge reserves, where nothing can be saved"
                : "must be a folder path from the project root";

            entryErrors.Add(new ProjectConfigEntryError(
                $"{CelbridgeSectionName}.{ResourcesKey}",
                $"'{DownloadsFolderKey}' value '{downloadsFolder}' {problem}. The key was dropped."));

            return string.Empty;
        }

        return folder.Path;
    }

    private static ContributionOverride? ParseContributionEntry(
        TomlTable entryTable,
        int entryIndex,
        List<ProjectConfigEntryError> entryErrors)
    {
        var entryName = $"[[{ContributionSectionName}]] #{entryIndex}";

        var packageName = ReadString(entryTable, ContributionPropertyKeys.Package);
        if (string.IsNullOrWhiteSpace(packageName))
        {
            entryErrors.Add(new ProjectConfigEntryError(
                entryName, $"Missing required '{ContributionPropertyKeys.Package}' key. The entry was skipped."));
            return null;
        }

        var contributionId = ReadString(entryTable, ContributionPropertyKeys.Contribution);
        if (string.IsNullOrWhiteSpace(contributionId))
        {
            entryErrors.Add(new ProjectConfigEntryError(
                entryName, $"Missing required '{ContributionPropertyKeys.Contribution}' key. The entry was skipped."));
            return null;
        }

        var reference = $"{packageName}/{contributionId}";
        var disabled = ReadOptionalBool(entryTable, ContributionPropertyKeys.Disabled, reference, entryErrors) ?? false;
        var enabled = ReadOptionalBool(entryTable, ContributionPropertyKeys.Enabled, reference, entryErrors) ?? false;

        // Every non-reserved key is contribution configuration, kept as its raw TOML value for
        // descriptor type-checking at workspace load.
        var config = new Dictionary<string, object?>();
        foreach (var (key, value) in entryTable)
        {
            if (ContributionPropertyKeys.All.Contains(key))
            {
                continue;
            }

            switch (value)
            {
                case string or bool or long or double:
                    config[key] = value;
                    break;

                case TomlArray array:
                    if (TomlValueConverter.TryConvertStringList(array, out var items))
                    {
                        config[key] = items;
                    }
                    else
                    {
                        entryErrors.Add(new ProjectConfigEntryError(
                            reference, $"Config key '{key}' must be a list of strings. The key was dropped."));
                    }
                    break;

                default:
                    entryErrors.Add(new ProjectConfigEntryError(
                        reference, $"Config key '{key}' has an unsupported value shape. The key was dropped."));
                    break;
            }
        }

        return new ContributionOverride
        {
            PackageName = packageName,
            ContributionId = contributionId,
            Disabled = disabled,
            Enabled = enabled,
            Config = config
        };
    }

    private static DocumentShortcut? ParseDocumentShortcutEntry(
        TomlTable entryTable,
        int entryIndex,
        List<ProjectConfigEntryError> entryErrors)
    {
        var entryName = $"[[{DocumentShortcutSectionName}]] #{entryIndex}";

        foreach (var key in ConfigSchemaHelper.FindUnknownKeys(entryTable.Keys, KnownDocumentShortcutKeys))
        {
            entryErrors.Add(new ProjectConfigEntryError(
                entryName, $"Unknown key '{key}'. The key was dropped."));
        }

        var resource = ReadString(entryTable, DocumentShortcutResourceKey);
        if (string.IsNullOrWhiteSpace(resource))
        {
            entryErrors.Add(new ProjectConfigEntryError(
                entryName, $"Missing required '{DocumentShortcutResourceKey}' key. The entry was skipped."));
            return null;
        }

        if (!ResourceKey.IsValidKey(resource))
        {
            entryErrors.Add(new ProjectConfigEntryError(
                entryName, $"'{DocumentShortcutResourceKey}' value '{resource}' is not a valid resource key. The entry was skipped."));
            return null;
        }

        var icon = ReadString(entryTable, DocumentShortcutIconKey);
        var area = ReadDocumentShortcutArea(entryTable, entryName, entryErrors);
        var hasRailButton = ReadOptionalBool(entryTable, DocumentShortcutRailButtonKey, entryName, entryErrors) ?? true;
        var openOnLoad = ReadOptionalBool(entryTable, DocumentShortcutOpenOnLoadKey, entryName, entryErrors) ?? false;

        return new DocumentShortcut
        {
            Resource = resource,
            Icon = icon ?? string.Empty,
            Area = area,
            HasRailButton = hasRailButton,
            OpenOnLoad = openOnLoad
        };
    }

    // The document area the shortcut opens into. An unusable value is reported and the shortcut falls back
    // to the main area, which still leaves a working button.
    private static WorkspaceArea ReadDocumentShortcutArea(
        TomlTable entryTable,
        string entryName,
        List<ProjectConfigEntryError> entryErrors)
    {
        var areaValue = ReadString(entryTable, DocumentShortcutAreaKey);
        if (string.IsNullOrEmpty(areaValue))
        {
            return WorkspaceArea.Main;
        }

        if (!WorkspaceAreaTokens.TryParse(areaValue, out var area))
        {
            entryErrors.Add(new ProjectConfigEntryError(
                entryName,
                $"'{DocumentShortcutAreaKey}' value '{areaValue}' is not a recognized document area " +
                $"({ValidDocumentShortcutAreaTokens}). The main area was used."));

            return WorkspaceArea.Main;
        }

        if (area == WorkspaceArea.Utility)
        {
            entryErrors.Add(new ProjectConfigEntryError(
                entryName,
                $"'{DocumentShortcutAreaKey}' names the Utility Panel, which holds no document tabs. " +
                $"The main area was used."));

            return WorkspaceArea.Main;
        }

        return area;
    }

    // Reads an optional boolean, returning null when the key is absent. A value of any other type is
    // reported and read as absent, so the caller's default applies.
    private static bool? ReadOptionalBool(
        TomlTable entryTable,
        string key,
        string reference,
        List<ProjectConfigEntryError> entryErrors)
    {
        if (!entryTable.TryGetValue(key, out var value))
        {
            return null;
        }

        if (value is bool boolValue)
        {
            return boolValue;
        }

        entryErrors.Add(new ProjectConfigEntryError(
            reference, $"'{key}' must be a boolean. The value was dropped."));

        return null;
    }

    // Returns the string value for the key, or null when the key is absent or
    // not a string. An empty string in the config is returned as-is so callers
    // can distinguish "set to empty" from "not set".
    private static string? ReadString(TomlTable table, string key)
    {
        if (table.TryGetValue(key, out var value)
            && value is string s)
        {
            return s;
        }
        return null;
    }

    private static IReadOnlyList<string>? ReadStringList(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value)
            || value is not TomlArray array)
        {
            return null;
        }

        var items = new List<string>(array.Count);
        foreach (var entry in array)
        {
            if (entry is string s)
            {
                items.Add(s);
            }
        }
        return items;
    }

}
