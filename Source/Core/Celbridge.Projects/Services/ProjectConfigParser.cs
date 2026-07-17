using Celbridge.Documents;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;

namespace Celbridge.Projects.Services;

/// <summary>
/// Static utility class for parsing Celbridge project configuration files (v2 schema).
/// Host-level declarations live on the [celbridge] table; every other top-level table declares
/// an editor instance. Malformed entries are skipped with a recorded entry error; a TOML syntax
/// error fails the whole parse.
/// </summary>
public static class ProjectConfigParser
{
    private const string PathSeparator = "/";
    private const string DefaultPythonVersion = "3.12";

    private const string CelbridgeSectionName = "celbridge";
    private const string ProjectSectionName = "project";
    private const string ShortcutSectionName = "shortcut";

    private const string CelbridgeVersionKey = "celbridge-version";
    private const string ProjectVersionKey = "project-version";
    private const string PackagesKey = "packages";
    private const string EditorAssociationsKey = "editor-associations";
    private const string FeaturesKey = "features";
    private const string ResourcesKey = "resources";

    private static readonly string[] KnownCelbridgeKeys =
    [
        CelbridgeVersionKey,
        ProjectVersionKey,
        PackagesKey,
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

    private static readonly string[] KnownProjectKeys =
    [
        "requires-python",
        "dependencies",
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

            // Tomlyn rejects bare-\r line terminators. Normalize once here so
            // a project config written with non-standard line endings still
            // parses cleanly.
            var text = LineEndingHelper.ConvertLineEndings(readResult.Value, "\n");
            var parse = SyntaxParser.Parse(text);
            if (parse.HasErrors)
            {
                var errors = string.Join("; ", parse.Diagnostics.Select(d => d.ToString()));
                return Result<ProjectConfig>.Fail($"TOML parse error(s): {errors}");
            }

            var root = TomlSerializer.Deserialize<TomlTable>(text);
            if (root is null)
            {
                return Result<ProjectConfig>.Fail($"Failed to deserialize TOML file: {configFilePath}");
            }

            var config = MapRootToModel(root);

            return Result<ProjectConfig>.Ok(config);
        }
        catch (Exception ex)
        {
            return Result<ProjectConfig>.Fail($"Failed to read TOML file: {configFilePath}")
                .WithException(ex);
        }
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

        var projectSection = new ProjectSection();
        if (root.TryGetValue(ProjectSectionName, out var projectObject) &&
            projectObject is TomlTable projectTable)
        {
            projectSection = ParseProjectTable(projectTable, entryErrors);
        }

        var shortcutsSection = new ShortcutsSection();
        if (root.TryGetValue(ShortcutSectionName, out var shortcutsObject) &&
            shortcutsObject is TomlTableArray shortcutsArray)
        {
            shortcutsSection = ParseShortcutsArray(shortcutsArray);
        }

        // Every remaining top-level table declares an editor instance. Tomlyn preserves the
        // file's table order, which carries the declaration-order semantics.
        var instances = new List<EditorInstanceDeclaration>();
        foreach (var (key, value) in root)
        {
            if (key == CelbridgeSectionName ||
                key == ProjectSectionName ||
                key == ShortcutSectionName)
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

            if (value is TomlTableArray)
            {
                entryErrors.Add(new ProjectConfigEntryError(
                    key, $"[[{key}]] is not a valid instance declaration. Declare an instance as a [{key}] table."));
                continue;
            }

            if (value is not TomlTable instanceTable)
            {
                entryErrors.Add(new ProjectConfigEntryError(
                    key, $"Top-level key '{key}' is not allowed. Host-level keys belong on the [{CelbridgeSectionName}] table."));
                continue;
            }

            var declaration = ParseInstanceTable(key, instanceTable, entryErrors);
            if (declaration is not null)
            {
                instances.Add(declaration);
            }
        }

        return new ProjectConfig
        {
            Celbridge = celbridgeSection,
            Project = projectSection,
            Shortcuts = shortcutsSection,
            Resources = resourcesSection,
            Features = featuresDict,
            Instances = instances,
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

        var packages = new List<string>();
        if (celbridgeTable.TryGetValue(PackagesKey, out var packagesObject))
        {
            if (packagesObject is TomlArray packagesArray)
            {
                foreach (var entry in packagesArray)
                {
                    if (entry is string packageName && !string.IsNullOrWhiteSpace(packageName))
                    {
                        packages.Add(packageName);
                    }
                    else
                    {
                        entryErrors.Add(new ProjectConfigEntryError(
                            CelbridgeSectionName, $"Ignored a non-string entry in '{PackagesKey}'."));
                    }
                }
            }
            else
            {
                entryErrors.Add(new ProjectConfigEntryError(
                    CelbridgeSectionName, $"'{PackagesKey}' must be an array of package names."));
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

                    if (!extension.StartsWith('.'))
                    {
                        entryErrors.Add(new ProjectConfigEntryError(
                            CelbridgeSectionName, $"'{EditorAssociationsKey}' key '{extension}' must be a file extension with a leading dot. The entry was ignored."));
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
            Packages = packages,
            EditorAssociations = editorAssociations
        };
    }

    private static ProjectSection ParseProjectTable(
        TomlTable projectTable,
        List<ProjectConfigEntryError> entryErrors)
    {
        foreach (var key in projectTable.Keys)
        {
            if (!KnownProjectKeys.Contains(key, StringComparer.Ordinal))
            {
                entryErrors.Add(new ProjectConfigEntryError(
                    ProjectSectionName, $"Unknown key '{key}' on [{ProjectSectionName}]. The key was ignored."));
            }
        }

        List<string>? dependencies = null;
        if (projectTable.TryGetValue("dependencies", out var dependenciesObject) &&
            dependenciesObject is TomlArray dependenciesArray)
        {
            dependencies = dependenciesArray.Select(x => x?.ToString() ?? string.Empty).ToList();
        }

        string? requiresPythonValue = null;
        if (projectTable.TryGetValue("requires-python", out var requiresPython))
        {
            requiresPythonValue = requiresPython?.ToString();
            if (requiresPythonValue == "<python-version>")
            {
                requiresPythonValue = DefaultPythonVersion;
            }
        }

        return new ProjectSection
        {
            RequiresPython = requiresPythonValue,
            Dependencies = dependencies
        };
    }

    private static EditorInstanceDeclaration? ParseInstanceTable(
        string instanceId,
        TomlTable instanceTable,
        List<ProjectConfigEntryError> entryErrors)
    {
        if (!EditorInstanceId.IsValidDeclaredName(instanceId))
        {
            entryErrors.Add(new ProjectConfigEntryError(
                instanceId, "Instance ids use only lowercase letters, digits, and hyphens. The instance was skipped."));
            return null;
        }

        var packageName = ReadString(instanceTable, InstancePropertyKeys.Package);
        if (string.IsNullOrWhiteSpace(packageName))
        {
            entryErrors.Add(new ProjectConfigEntryError(
                instanceId, $"Missing required '{InstancePropertyKeys.Package}' key. The instance was skipped."));
            return null;
        }

        var contributionId = ReadString(instanceTable, InstancePropertyKeys.Contribution);
        if (string.IsNullOrWhiteSpace(contributionId))
        {
            entryErrors.Add(new ProjectConfigEntryError(
                instanceId, $"Missing required '{InstancePropertyKeys.Contribution}' key. The instance was skipped."));
            return null;
        }

        var title = ReadDisplayOverride(instanceTable, InstancePropertyKeys.Title, instanceId, entryErrors);
        var icon = ReadDisplayOverride(instanceTable, InstancePropertyKeys.Icon, instanceId, entryErrors);
        var tooltip = ReadDisplayOverride(instanceTable, InstancePropertyKeys.Tooltip, instanceId, entryErrors);

        // Every non-reserved key is instance configuration, kept as its raw TOML value for
        // descriptor type-checking at workspace load.
        var config = new Dictionary<string, object?>();
        foreach (var (key, value) in instanceTable)
        {
            if (InstancePropertyKeys.All.Contains(key))
            {
                continue;
            }

            switch (value)
            {
                case string or bool or long or double:
                    config[key] = value;
                    break;

                case TomlArray array:
                    var items = new List<string>(array.Count);
                    var allStrings = true;
                    foreach (var entry in array)
                    {
                        if (entry is string stringEntry)
                        {
                            items.Add(stringEntry);
                        }
                        else
                        {
                            allStrings = false;
                            break;
                        }
                    }

                    if (allStrings)
                    {
                        config[key] = items;
                    }
                    else
                    {
                        entryErrors.Add(new ProjectConfigEntryError(
                            instanceId, $"Config key '{key}' must be a list of strings. The key was dropped."));
                    }
                    break;

                default:
                    entryErrors.Add(new ProjectConfigEntryError(
                        instanceId, $"Config key '{key}' has an unsupported value shape. The key was dropped."));
                    break;
            }
        }

        return new EditorInstanceDeclaration
        {
            InstanceId = instanceId,
            PackageName = packageName,
            ContributionId = contributionId,
            Title = title,
            Icon = icon,
            Tooltip = tooltip,
            Config = config
        };
    }

    // Reads an optional string-valued display override, reporting and dropping a value of any
    // other type so a typo degrades one setting rather than the instance.
    private static string? ReadDisplayOverride(
        TomlTable instanceTable,
        string key,
        string instanceId,
        List<ProjectConfigEntryError> entryErrors)
    {
        if (!instanceTable.TryGetValue(key, out var value))
        {
            return null;
        }

        if (value is string stringValue && !string.IsNullOrWhiteSpace(stringValue))
        {
            return stringValue;
        }

        entryErrors.Add(new ProjectConfigEntryError(
            instanceId, $"'{key}' must be a non-empty string. The key was dropped."));

        return null;
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

    private static ShortcutsSection ParseShortcutsArray(TomlTableArray shortcutsArray)
    {
        var definitions = new List<ShortcutDefinition>();
        var validationErrors = new List<ShortcutValidationError>();

        for (int i = 0; i < shortcutsArray.Count; i++)
        {
            var shortcutTable = shortcutsArray[i];
            var shortcutIndex = i + 1;

            string? name = null;
            if (shortcutTable.TryGetValue("name", out var nameObj) && nameObj is string nameStr)
            {
                name = nameStr;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                validationErrors.Add(new ShortcutValidationError(shortcutIndex, "name", "The 'name' property is required and cannot be empty."));
                continue;
            }

            if (name.StartsWith(PathSeparator) || name.EndsWith(PathSeparator))
            {
                validationErrors.Add(new ShortcutValidationError(shortcutIndex, "name", $"The 'name' property cannot start or end with '{PathSeparator}'."));
                continue;
            }

            if (name.Contains($"{PathSeparator}{PathSeparator}"))
            {
                validationErrors.Add(new ShortcutValidationError(shortcutIndex, "name", $"The 'name' property cannot contain empty segments (consecutive '{PathSeparator}' characters)."));
                continue;
            }

            string? icon = null;
            if (shortcutTable.TryGetValue("icon", out var iconObj) && iconObj is string iconStr)
            {
                icon = iconStr;
            }

            string? tooltip = null;
            if (shortcutTable.TryGetValue("tooltip", out var tooltipObj) && tooltipObj is string tooltipStr)
            {
                tooltip = tooltipStr;
            }

            string? script = null;
            if (shortcutTable.TryGetValue("script", out var scriptObj) && scriptObj is string scriptStr)
            {
                script = scriptStr;
            }

            var definition = new ShortcutDefinition
            {
                Name = name,
                Icon = icon,
                Tooltip = tooltip,
                Script = script
            };

            definitions.Add(definition);
        }

        // Second pass validation: check that all parent paths exist as groups
        var groupPaths = new HashSet<string>();
        foreach (var def in definitions)
        {
            if (def.IsGroup)
            {
                groupPaths.Add(def.Name);
            }
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            var def = definitions[i];
            var parentPath = def.ParentPath;

            if (parentPath != null)
            {
                if (!groupPaths.Contains(parentPath))
                {
                    var pathSegments = parentPath.Split(PathSeparator);
                    var currentPath = "";
                    bool foundValidParent = false;

                    foreach (var segment in pathSegments)
                    {
                        currentPath = string.IsNullOrEmpty(currentPath) ? segment : $"{currentPath}{PathSeparator}{segment}";
                        if (groupPaths.Contains(currentPath))
                        {
                            foundValidParent = true;
                        }
                    }

                    if (!foundValidParent)
                    {
                        validationErrors.Add(new ShortcutValidationError(
                            i + 1,
                            "name",
                            $"The parent path '{parentPath}' does not exist. Define a group with name='{parentPath}' first."));
                    }
                }
            }
        }

        // Validate that groups have at least one child
        var usedParentPaths = new HashSet<string>();
        foreach (var def in definitions)
        {
            var parentPath = def.ParentPath;
            if (parentPath != null)
            {
                var pathSegments = parentPath.Split(PathSeparator);
                var currentPath = "";
                foreach (var segment in pathSegments)
                {
                    currentPath = string.IsNullOrEmpty(currentPath) ? segment : $"{currentPath}{PathSeparator}{segment}";
                    usedParentPaths.Add(currentPath);
                }
            }
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            var def = definitions[i];
            if (def.IsGroup && !usedParentPaths.Contains(def.Name))
            {
                validationErrors.Add(new ShortcutValidationError(
                    i + 1,
                    "script",
                    $"Group '{def.DisplayName}' has no children. Either add child items with names starting with '{def.Name}/' or add a script to make it a command."));
            }
        }

        return new ShortcutsSection
        {
            Definitions = definitions,
            ValidationErrors = validationErrors
        };
    }
}
