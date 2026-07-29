using Celbridge.Utilities;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;

namespace Celbridge.Projects.Services;

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

    private const string CelbridgeVersionKey = "celbridge-version";
    private const string ProjectVersionKey = "project-version";
    private const string DescriptionKey = "description";
    private const string DisabledPackagesKey = "disabled-packages";
    private const string EditorAssociationsKey = "editor-associations";
    private const string FeaturesKey = "features";
    private const string ResourcesKey = "resources";

    private static readonly string[] KnownCelbridgeKeys =
    [
        CelbridgeVersionKey,
        ProjectVersionKey,
        DescriptionKey,
        DisabledPackagesKey,
        EditorAssociationsKey,
        FeaturesKey,
        ResourcesKey,
    ];

    private static readonly string[] KnownResourcesKeys =
    [
        "ignore-file",
        "add",
        "remove",
        "lock",
    ];

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
        var featuresDict = new Dictionary<string, bool>();

        if (root.TryGetValue(CelbridgeSectionName, out var celbridgeObject) &&
            celbridgeObject is TomlTable celbridgeTable)
        {
            celbridgeSection = ParseCelbridgeTable(celbridgeTable, entryErrors, out resourcesSection, out featuresDict);
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
                    $"'{ContributionSectionName}' must be declared as [[{ContributionSectionName}]] entries. The section was ignored."));
            }
        }

        // Any other top-level key is not part of the schema.
        foreach (var (key, _) in root)
        {
            if (key == CelbridgeSectionName ||
                key == ContributionSectionName)
            {
                continue;
            }

            if (key == FeaturesKey ||
                key == ResourcesKey)
            {
                entryErrors.Add(new ProjectConfigEntryError(
                    key, $"The top-level [{key}] section has moved to [{CelbridgeSectionName}]. The section was ignored."));
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
            Features = featuresDict,
            ContributionOverrides = contributions,
            EntryErrors = entryErrors
        };
    }

    private static CelbridgeSection ParseCelbridgeTable(
        TomlTable celbridgeTable,
        List<ProjectConfigEntryError> entryErrors,
        out ResourcesSection resourcesSection,
        out Dictionary<string, bool> featuresDict)
    {
        resourcesSection = new ResourcesSection();
        featuresDict = new Dictionary<string, bool>();

        foreach (var key in celbridgeTable.Keys)
        {
            if (!KnownCelbridgeKeys.Contains(key, StringComparer.Ordinal))
            {
                entryErrors.Add(new ProjectConfigEntryError(
                    CelbridgeSectionName, $"Unknown key '{key}' on [{CelbridgeSectionName}]. The key was ignored."));
            }
        }

        var disabledPackages = new List<string>();
        if (celbridgeTable.TryGetValue(DisabledPackagesKey, out var disabledPackagesObject))
        {
            if (disabledPackagesObject is TomlArray disabledPackagesArray)
            {
                foreach (var entry in disabledPackagesArray)
                {
                    if (entry is string packageName && !string.IsNullOrWhiteSpace(packageName))
                    {
                        disabledPackages.Add(packageName);
                    }
                    else
                    {
                        entryErrors.Add(new ProjectConfigEntryError(
                            CelbridgeSectionName, $"Ignored a non-string entry in '{DisabledPackagesKey}'."));
                    }
                }
            }
            else
            {
                entryErrors.Add(new ProjectConfigEntryError(
                    CelbridgeSectionName, $"'{DisabledPackagesKey}' must be an array of package names."));
            }
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
                            CelbridgeSectionName, $"'{EditorAssociationsKey}' entry '{extension}' must name an editor id. The entry was ignored."));
                        continue;
                    }

                    if (!FileExtensionUtils.IsWellFormedFileExtension(extension))
                    {
                        entryErrors.Add(new ProjectConfigEntryError(
                            CelbridgeSectionName, $"'{EditorAssociationsKey}' key '{extension}' must be a well-formed file extension (e.g. \".txt\"). The entry was ignored."));
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
                            CelbridgeSectionName, $"'{FeaturesKey}' entry '{featureKey}' must be a boolean. The entry was ignored."));
                    }
                }
            }
            else
            {
                entryErrors.Add(new ProjectConfigEntryError(
                    CelbridgeSectionName, $"'{FeaturesKey}' must be an inline table of feature flags."));
            }
        }

        if (celbridgeTable.TryGetValue(ResourcesKey, out var resourcesObject))
        {
            if (resourcesObject is TomlTable resourcesTable)
            {
                // A flat key hand-appended after the [celbridge.resources] header lands in this
                // table per TOML rules. Unknown keys are reported so the mistake fails loud
                // rather than silently re-parenting.
                foreach (var key in resourcesTable.Keys)
                {
                    if (!KnownResourcesKeys.Contains(key, StringComparer.Ordinal))
                    {
                        entryErrors.Add(new ProjectConfigEntryError(
                            $"{CelbridgeSectionName}.{ResourcesKey}",
                            $"Unknown key '{key}' on [{CelbridgeSectionName}.{ResourcesKey}]. Flat [{CelbridgeSectionName}] keys must precede the [{CelbridgeSectionName}.{ResourcesKey}] header."));
                    }
                }

                resourcesSection = resourcesSection with
                {
                    IgnoreFile = ReadString(resourcesTable, "ignore-file") ?? resourcesSection.IgnoreFile,
                    Add = ReadStringList(resourcesTable, "add") ?? resourcesSection.Add,
                    Remove = ReadStringList(resourcesTable, "remove") ?? resourcesSection.Remove,
                    Lock = ReadStringList(resourcesTable, "lock") ?? resourcesSection.Lock,
                };
            }
            else
            {
                entryErrors.Add(new ProjectConfigEntryError(
                    CelbridgeSectionName, $"'{ResourcesKey}' must be a [{CelbridgeSectionName}.{ResourcesKey}] table."));
            }
        }

        return new CelbridgeSection
        {
            CelbridgeVersion = ReadString(celbridgeTable, CelbridgeVersionKey),
            ProjectVersion = ReadString(celbridgeTable, ProjectVersionKey),
            Description = ReadString(celbridgeTable, DescriptionKey),
            DisabledPackages = disabledPackages,
            EditorAssociations = editorAssociations
        };
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
        var disabled = ReadBoolFlag(entryTable, ContributionPropertyKeys.Disabled, reference, entryErrors);
        var enabled = ReadBoolFlag(entryTable, ContributionPropertyKeys.Enabled, reference, entryErrors);

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

    // Reads an optional boolean activation flag, reporting and ignoring a value of any other type.
    private static bool ReadBoolFlag(
        TomlTable entryTable,
        string key,
        string reference,
        List<ProjectConfigEntryError> entryErrors)
    {
        if (!entryTable.TryGetValue(key, out var value))
        {
            return false;
        }

        if (value is bool boolValue)
        {
            return boolValue;
        }

        entryErrors.Add(new ProjectConfigEntryError(
            reference, $"'{key}' must be a boolean. The value was ignored."));

        return false;
    }

    // Returns the string value for the key, or null when the key is absent or
    // not a string. An empty string in the config is returned as-is so callers
    // can distinguish "set to empty" from "not set" (e.g. ignore-file = "").
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
