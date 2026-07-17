using System.Text.Json;
using Celbridge.Documents;
using Celbridge.Projects;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;

namespace Celbridge.Packages;

/// <summary>
/// Parses package.toml and referenced editor manifests (*.editor.toml) to produce
/// EditorContribution objects. Handles the two-level manifest structure: package identity +
/// editor contributions.
/// </summary>
public static class PackageManifestLoader
{
    private const string PackageSection = "package";
    private const string ContributesSection = "contributes";
    private const string PermissionsSection = "permissions";
    private const string EditorSection = "editor";
    private const string EditorsKey = "editors";
    private const string FileTypesSection = "file-types";
    private const string TemplatesSection = "templates";
    private const string OptionsSection = "options";
    private const string UtilitySection = "utility";
    private const string ConfigSection = "config";
    private const string ToolsKey = "tools";

    private const string IdKey = "id";
    private const string NameKey = "name";
    private const string TitleKey = "title";
    private const string TypeKey = "type";
    private const string ExtensionKey = "extension";
    private const string ExtensionsFileKey = "extensions-file";
    private const string DisplayNameKey = "display-name";
    private const string DescriptionKey = "description";
    private const string TemplateFileKey = "template-file";
    private const string DefaultKey = "default";
    private const string ValuesKey = "values";
    private const string KeyKey = "key";
    private const string EntryPointKey = "entry-point";
    private const string BinaryKey = "binary";
    private const string ExternalContentKey = "external-content";
    private const string ResourceExtensionKey = "resource-extension";
    private const string TemplateKey = "template";
    private const string IconKey = "icon";
    private const string TooltipKey = "tooltip";
    private const string LazyLoadKey = "lazy-load";

    private const string DocumentTypeValue = "document";
    private const string UtilityTypeValue = "utility";
    private const string DefaultEntryPoint = "index.html";

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

            var packageName = GetString(packageTable, NameKey);
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

            var packageTitle = GetString(packageTable, TitleKey);

            var permittedTools = Array.Empty<string>() as IReadOnlyList<string>;
            if (root.TryGetValue(PermissionsSection, out var permissionsObject) &&
                permissionsObject is TomlTable permissionsTable)
            {
                permittedTools = GetStringArray(permissionsTable, ToolsKey);
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
                if (contributesTable.TryGetValue(EditorsKey, out var editorsObject) &&
                    editorsObject is TomlArray editorsArray)
                {
                    foreach (var editorEntry in editorsArray)
                    {
                        if (editorEntry is string editorManifestPath)
                        {
                            editorManifestPaths.Add(editorManifestPath);
                        }
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
                var loadResult = LoadEditor(fullPath, packageInfo, reader);
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

    /// <summary>
    /// Parses a single editor manifest into an EditorContribution.
    /// </summary>
    private static Result<EditorContribution> LoadEditor(
        string editorTomlPath,
        PackageInfo packageInfo,
        IPackageReader reader)
    {
        try
        {
            if (!reader.Exists(editorTomlPath))
            {
                return Result.Fail($"Editor manifest not found: {editorTomlPath}");
            }

            var readResult = reader.ReadAllText(editorTomlPath);
            if (readResult.IsFailure)
            {
                return Result.Fail($"Failed to read editor manifest: {editorTomlPath}")
                    .WithErrors(readResult);
            }
            var toml = readResult.Value;
            var parsed = SyntaxParser.Parse(toml);

            if (parsed.HasErrors)
            {
                var errors = string.Join("; ", parsed.Diagnostics.Select(d => d.ToString()));
                return Result.Fail($"TOML parse error in {editorTomlPath}: {errors}");
            }

            var root = TomlSerializer.Deserialize<TomlTable>(toml);
            if (root is null)
            {
                return Result.Fail($"Failed to deserialize editor manifest: {editorTomlPath}");
            }

            if (!root.TryGetValue(EditorSection, out var editorObject) ||
                editorObject is not TomlTable editorTable)
            {
                return Result.Fail($"Missing [{EditorSection}] section: {editorTomlPath}");
            }

            var editorId = GetString(editorTable, IdKey);
            if (string.IsNullOrEmpty(editorId))
            {
                return Result.Fail($"Editor missing required '{IdKey}' field: {editorTomlPath}");
            }

            if (!EditorInstanceId.IsValidDeclaredName(editorId))
            {
                return Result.Fail(
                    $"Invalid editor id '{editorId}' in manifest: {editorTomlPath}. " +
                    $"Editor ids use only lowercase letters, digits, and hyphens.");
            }

            var editorType = GetStringOrNull(editorTable, TypeKey);
            if (string.IsNullOrEmpty(editorType))
            {
                return Result.Fail(
                    $"Editor missing required '{TypeKey}' field: {editorTomlPath}. " +
                    $"Valid values are \"{DocumentTypeValue}\" and \"{UtilityTypeValue}\".");
            }

            if (editorType != DocumentTypeValue &&
                editorType != UtilityTypeValue)
            {
                return Result.Fail(
                    $"Unknown editor type '{editorType}': {editorTomlPath}. " +
                    $"Valid values are \"{DocumentTypeValue}\" and \"{UtilityTypeValue}\".");
            }

            // Per-type section validation: the type names the sections the manifest must and
            // must not declare.
            var hasUtilitySection = root.ContainsKey(UtilitySection);
            var hasFileTypesSection = root.ContainsKey(FileTypesSection);

            UtilityDescriptor? utilityDescriptor = null;
            if (editorType == UtilityTypeValue)
            {
                if (!hasUtilitySection)
                {
                    return Result.Fail(
                        $"'{TypeKey} = \"{UtilityTypeValue}\"' requires a [{UtilitySection}] section: {editorTomlPath}");
                }
                if (hasFileTypesSection)
                {
                    return Result.Fail(
                        $"'{TypeKey} = \"{UtilityTypeValue}\"' forbids [[{FileTypesSection}]]: {editorTomlPath}");
                }

                if (root[UtilitySection] is not TomlTable utilityTable)
                {
                    return Result.Fail($"[{UtilitySection}] must be a table: {editorTomlPath}");
                }

                var utilityResult = ParseUtilitySection(utilityTable, editorTomlPath);
                if (utilityResult.IsFailure)
                {
                    return Result<EditorContribution>.Fail(utilityResult.FirstErrorMessage)
                        .WithErrors(utilityResult);
                }
                utilityDescriptor = utilityResult.Value;
            }
            else
            {
                if (hasUtilitySection)
                {
                    return Result.Fail(
                        $"'{TypeKey} = \"{DocumentTypeValue}\"' forbids a [{UtilitySection}] section: {editorTomlPath}");
                }
                if (!hasFileTypesSection)
                {
                    return Result.Fail(
                        $"'{TypeKey} = \"{DocumentTypeValue}\"' requires at least one [[{FileTypesSection}]] entry: {editorTomlPath}");
                }
            }

            var displayName = GetString(editorTable, DisplayNameKey);
            if (string.IsNullOrEmpty(displayName))
            {
                if (utilityDescriptor is not null)
                {
                    // A utility has no separate label field, so its tooltip localization key doubles as the
                    // editor display name used for the tab title and any diagnostics.
                    displayName = utilityDescriptor.Tooltip;
                }
                else
                {
                    return Result.Fail(
                        $"Editor missing required '{DisplayNameKey}' field in [{EditorSection}] section: {editorTomlPath}. " +
                        $"Supply a localization key or plain string for the editor's label in the Reopen-with dialog.");
                }
            }

            var fileTypes = new List<EditorFileType>();
            if (root.TryGetValue(FileTypesSection, out var fileTypesObject) &&
                fileTypesObject is TomlTableArray fileTypesArray)
            {
                foreach (var fileTypeTable in fileTypesArray)
                {
                    var fileTypeDisplayName = GetString(fileTypeTable, DisplayNameKey);
                    if (string.IsNullOrEmpty(fileTypeDisplayName))
                    {
                        return Result.Fail(
                            $"File type missing required '{DisplayNameKey}' field in [[{FileTypesSection}]] entry: {editorTomlPath}. " +
                            $"Supply a localization key or plain string naming the file type (e.g., the noun shown in the Reopen-with dialog).");
                    }

                    var extensionLiteral = GetStringOrNull(fileTypeTable, ExtensionKey);
                    var extensionsFilePath = GetStringOrNull(fileTypeTable, ExtensionsFileKey);

                    if (!string.IsNullOrEmpty(extensionsFilePath))
                    {
                        if (!string.IsNullOrEmpty(extensionLiteral))
                        {
                            return Result.Fail(
                                $"A [[{FileTypesSection}]] entry cannot specify both '{ExtensionKey}' and '{ExtensionsFileKey}': {editorTomlPath}");
                        }

                        var expandResult = ExpandExtensionsFile(packageInfo.PackageFolder, extensionsFilePath, fileTypeDisplayName, reader);
                        if (expandResult.IsFailure)
                        {
                            return Result.Fail($"Failed to expand '{ExtensionsFileKey}' in {editorTomlPath}")
                                .WithErrors(expandResult);
                        }

                        fileTypes.AddRange(expandResult.Value);
                    }
                    else
                    {
                        fileTypes.Add(new EditorFileType
                        {
                            FileExtension = extensionLiteral ?? string.Empty,
                            DisplayName = fileTypeDisplayName
                        });
                    }
                }
            }

            if (utilityDescriptor is null &&
                fileTypes.Count == 0)
            {
                return Result.Fail($"A document editor must declare at least one file type: {editorTomlPath}");
            }

            var templates = new List<DocumentTemplate>();
            if (root.TryGetValue(TemplatesSection, out var templatesObject) &&
                templatesObject is TomlTableArray templatesArray)
            {
                foreach (var templateTable in templatesArray)
                {
                    templates.Add(new DocumentTemplate
                    {
                        Id = GetString(templateTable, IdKey),
                        DisplayName = GetString(templateTable, DisplayNameKey),
                        TemplateFile = GetString(templateTable, TemplateFileKey),
                        Default = GetBool(templateTable, DefaultKey)
                    });
                }
            }

            // An editor with external-content = true sources its content from outside the file bytes,
            // so a starter template would never be written to disk.
            if (templates.Count > 0 &&
                (GetBoolOrNull(editorTable, ExternalContentKey) ?? false))
            {
                return Result.Fail(
                    $"Editor manifest '{editorTomlPath}' declares both '{ExternalContentKey} = true' and [[{TemplatesSection}]]. " +
                    $"Templates cannot be used with external content.");
            }

            var descriptorsResult = ParseConfigDescriptors(root, editorTomlPath);
            if (descriptorsResult.IsFailure)
            {
                return Result<EditorContribution>.Fail(descriptorsResult.FirstErrorMessage)
                    .WithErrors(descriptorsResult);
            }
            var configDescriptors = descriptorsResult.Value;

            var contribution = BuildContribution(root, packageInfo, editorId, displayName, fileTypes, templates, configDescriptors, editorTable, utilityDescriptor);

            return Result<EditorContribution>.Ok(contribution);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to load editor manifest: {editorTomlPath}").WithException(ex);
        }
    }

    private static EditorContribution BuildContribution(
        TomlTable root,
        PackageInfo packageInfo,
        string editorId,
        string displayName,
        List<EditorFileType> fileTypes,
        List<DocumentTemplate> templates,
        List<ConfigDescriptor> configDescriptors,
        TomlTable editorTable,
        UtilityDescriptor? utilityDescriptor)
    {
        var entryPoint = GetStringOrNull(editorTable, EntryPointKey) ?? DefaultEntryPoint;
        var binary = GetBoolOrNull(editorTable, BinaryKey) ?? false;
        var externalContent = GetBoolOrNull(editorTable, ExternalContentKey) ?? false;

        var options = ParseOptionsTable(root);

        return new EditorContribution
        {
            Package = packageInfo,
            Id = editorId,
            DisplayName = displayName,
            FileTypes = fileTypes.AsReadOnly(),
            Templates = templates.AsReadOnly(),
            EntryPoint = entryPoint,
            Binary = binary,
            ExternalContent = externalContent,
            Options = options,
            ConfigDescriptors = configDescriptors.AsReadOnly(),
            UtilityDescriptor = utilityDescriptor
        };
    }

    private static Result<UtilityDescriptor> ParseUtilitySection(TomlTable utilityTable, string editorTomlPath)
    {
        var resourceExtension = GetString(utilityTable, ResourceExtensionKey);
        if (string.IsNullOrEmpty(resourceExtension))
        {
            return Result.Fail($"[{UtilitySection}] missing required '{ResourceExtensionKey}' field: {editorTomlPath}");
        }

        if (!resourceExtension.StartsWith('.') ||
            resourceExtension.Length < 2)
        {
            return Result.Fail(
                $"[{UtilitySection}] '{ResourceExtensionKey}' value '{resourceExtension}' must be a file extension with a leading dot: {editorTomlPath}");
        }

        var icon = GetString(utilityTable, IconKey);
        if (string.IsNullOrEmpty(icon))
        {
            return Result.Fail($"[{UtilitySection}] missing required '{IconKey}' field: {editorTomlPath}");
        }

        var tooltip = GetString(utilityTable, TooltipKey);
        if (string.IsNullOrEmpty(tooltip))
        {
            return Result.Fail($"[{UtilitySection}] missing required '{TooltipKey}' field: {editorTomlPath}");
        }

        var template = GetStringOrNull(utilityTable, TemplateKey) ?? string.Empty;
        var lazyLoad = GetBoolOrNull(utilityTable, LazyLoadKey) ?? false;

        var descriptor = new UtilityDescriptor
        {
            ResourceExtension = resourceExtension.ToLowerInvariant(),
            Template = template,
            Icon = icon,
            Tooltip = tooltip,
            LazyLoad = lazyLoad
        };

        return descriptor;
    }

    private static Result<List<ConfigDescriptor>> ParseConfigDescriptors(TomlTable root, string editorTomlPath)
    {
        var descriptors = new List<ConfigDescriptor>();
        if (!root.TryGetValue(ConfigSection, out var configObject))
        {
            return descriptors;
        }

        if (configObject is not TomlTableArray configArray)
        {
            return Result.Fail($"[[{ConfigSection}]] must be an array of tables: {editorTomlPath}");
        }

        foreach (var configTable in configArray)
        {
            var key = GetString(configTable, KeyKey);
            if (string.IsNullOrEmpty(key))
            {
                return Result.Fail($"Config descriptor missing required '{KeyKey}' field: {editorTomlPath}");
            }

            if (!EditorInstanceId.IsValidDeclaredName(key))
            {
                return Result.Fail(
                    $"Config descriptor key '{key}' must use only lowercase letters, digits, and hyphens: {editorTomlPath}");
            }

            // Reserved names are checked at package load so the error reaches the package
            // author, never a project.
            if (InstancePropertyKeys.All.Contains(key))
            {
                return Result.Fail(
                    $"Config descriptor key '{key}' collides with a reserved instance property: {editorTomlPath}");
            }

            if (descriptors.Any(d => string.Equals(d.Key, key, StringComparison.Ordinal)))
            {
                return Result.Fail($"Duplicate config descriptor key '{key}': {editorTomlPath}");
            }

            var typeValue = GetStringOrNull(configTable, TypeKey);
            var descriptorType = typeValue switch
            {
                "bool" => ConfigValueType.Bool,
                "string" => ConfigValueType.String,
                "number" => ConfigValueType.Number,
                "enum" => ConfigValueType.Enum,
                "string-list" => ConfigValueType.StringList,
                _ => (ConfigValueType?)null
            };
            if (descriptorType is null)
            {
                return Result.Fail(
                    $"Config descriptor '{key}' has unknown type '{typeValue}': {editorTomlPath}. " +
                    $"Valid types are \"bool\", \"string\", \"number\", \"enum\", and \"string-list\".");
            }

            var values = GetStringArray(configTable, ValuesKey);
            if (descriptorType == ConfigValueType.Enum)
            {
                if (values.Count == 0)
                {
                    return Result.Fail(
                        $"Config descriptor '{key}' of type \"enum\" requires a non-empty '{ValuesKey}' list: {editorTomlPath}");
                }
            }
            else if (configTable.ContainsKey(ValuesKey))
            {
                return Result.Fail(
                    $"Config descriptor '{key}' declares '{ValuesKey}' but is not of type \"enum\": {editorTomlPath}");
            }

            var displayName = GetString(configTable, DisplayNameKey);
            if (string.IsNullOrEmpty(displayName))
            {
                return Result.Fail($"Config descriptor '{key}' missing required '{DisplayNameKey}' field: {editorTomlPath}");
            }

            var description = GetString(configTable, DescriptionKey);

            var descriptor = new ConfigDescriptor
            {
                Key = key,
                Type = descriptorType.Value,
                Values = values,
                DisplayName = displayName,
                Description = description
            };

            if (configTable.TryGetValue(DefaultKey, out var defaultObject))
            {
                var rawDefault = NormalizeTomlValue(defaultObject);
                var encodeResult = ConfigValueEncoder.Encode(rawDefault, descriptor);
                if (encodeResult.IsFailure)
                {
                    return Result.Fail($"Config descriptor '{key}' has an invalid '{DefaultKey}' value: {editorTomlPath}")
                        .WithErrors(encodeResult);
                }
                var encodedDefault = encodeResult.Value;

                descriptor = descriptor with { DefaultValue = encodedDefault };
            }

            descriptors.Add(descriptor);
        }

        return descriptors;
    }

    // Converts a TOML value into the closed raw-value set shared with the project config parser:
    // string, bool, long, double, or IReadOnlyList of string. Other shapes pass through and fail
    // descriptor type-checking with a clear message.
    private static object? NormalizeTomlValue(object? value)
    {
        if (value is TomlArray array)
        {
            var items = new List<string>(array.Count);
            foreach (var entry in array)
            {
                if (entry is not string stringEntry)
                {
                    return value;
                }
                items.Add(stringEntry);
            }
            return items;
        }

        return value;
    }

    private static IReadOnlyDictionary<string, string> ParseOptionsTable(TomlTable root)
    {
        if (!root.TryGetValue(OptionsSection, out var optionsObject) ||
            optionsObject is not TomlTable optionsTable)
        {
            return new Dictionary<string, string>();
        }

        var result = new Dictionary<string, string>();
        foreach (var entry in optionsTable)
        {
            var stringValue = entry.Value switch
            {
                string s => s,
                bool b => b ? "true" : "false",
                long l => l.ToString(System.Globalization.CultureInfo.InvariantCulture),
                double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => null
            };

            if (stringValue is not null)
            {
                result[entry.Key] = stringValue;
            }
        }

        return result;
    }

    private static Result<List<EditorFileType>> ExpandExtensionsFile(
        string packageFolder,
        string relativePath,
        string displayName,
        IPackageReader reader)
    {
        var fullPath = Path.Combine(packageFolder, relativePath);
        if (!reader.Exists(fullPath))
        {
            return Result.Fail($"Extensions file not found: {fullPath}");
        }

        try
        {
            var readResult = reader.ReadAllText(fullPath);
            if (readResult.IsFailure)
            {
                return Result.Fail($"Failed to read extensions file: {fullPath}")
                    .WithErrors(readResult);
            }
            var json = readResult.Value;
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Result.Fail($"Extensions file must be a JSON object with extension keys: {fullPath}");
            }

            var result = new List<EditorFileType>();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                result.Add(new EditorFileType
                {
                    FileExtension = property.Name,
                    DisplayName = displayName
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to parse extensions file: {fullPath}").WithException(ex);
        }
    }

    private static string GetString(TomlTable table, string key)
    {
        if (table.TryGetValue(key, out var value) && value is string str)
        {
            return str;
        }

        return string.Empty;
    }

    private static string? GetStringOrNull(TomlTable table, string key)
    {
        if (table.TryGetValue(key, out var value) && value is string str)
        {
            return str;
        }

        return null;
    }

    private static bool GetBool(TomlTable table, string key)
    {
        if (table.TryGetValue(key, out var value) && value is bool b)
        {
            return b;
        }

        return false;
    }

    private static bool? GetBoolOrNull(TomlTable table, string key)
    {
        if (table.TryGetValue(key, out var value) && value is bool b)
        {
            return b;
        }

        return null;
    }

    private static IReadOnlyList<string> GetStringArray(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value) || value is not TomlArray array)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>(array.Count);
        foreach (var element in array)
        {
            if (element is string str && !string.IsNullOrEmpty(str))
            {
                result.Add(str);
            }
        }

        return result.AsReadOnly();
    }
}
