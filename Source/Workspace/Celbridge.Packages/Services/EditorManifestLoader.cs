using Celbridge.Documents;
using Celbridge.Projects;
using Celbridge.Utilities;
using Celbridge.Workspace;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;

namespace Celbridge.Packages;

/// <summary>
/// Parses a single editor manifest (*.editor.toml) into an EditorContribution: the [editor] section,
/// its type-specific sections ([[file-types]] for a document, [utility] for a utility), templates,
/// options, and [[config]] descriptors.
/// </summary>
internal static class EditorManifestLoader
{
    private const string EditorSection = "editor";
    private const string FileTypesSection = "file-types";
    private const string TemplatesSection = "templates";
    private const string OptionsSection = "options";
    private const string UtilitySection = "utility";
    private const string ConfigSection = "config";

    private const string IdKey = "id";
    private const string TypeKey = "type";
    private const string ExtensionKey = "extension";
    private const string FromCatalogKey = "from-catalog";
    private const string DisplayNameKey = "display-name";
    private const string DescriptionKey = "description";
    private const string TemplateFileKey = "template-file";
    private const string DefaultKey = "default";
    private const string ValuesKey = "values";
    private const string KeyKey = "key";
    private const string EntryPointKey = "entry-point";
    private const string BinaryKey = "binary";
    private const string ExternalContentKey = "external-content";
    private const string ActivationKey = "activation";
    private const string RequiredActivationValue = "required";
    private const string RecommendedActivationValue = "recommended";
    private const string OptionalActivationValue = "optional";
    private const string ResourceExtensionKey = "resource-extension";
    private const string TemplateKey = "template";
    private const string IconKey = "icon";
    private const string IconColorKey = "icon-color";
    private const string IconScaleKey = "icon-scale";
    private const string DockAreaKey = "dock-area";
    private const string NoDockAreaValue = "none";

    private const string CatalogLanguagesValue = "languages";

    // The sections a manifest declares, and the fields each one defines. A key outside these sets is
    // reported as an unknown field. [options] is absent because its keys are the editor's own, passed
    // through to the editor rather than interpreted here.
    private static readonly IReadOnlySet<string> RootKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        EditorSection,
        FileTypesSection,
        TemplatesSection,
        OptionsSection,
        UtilitySection,
        ConfigSection
    };

    private static readonly IReadOnlySet<string> EditorKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        IdKey,
        TypeKey,
        DisplayNameKey,
        DescriptionKey,
        EntryPointKey,
        BinaryKey,
        ExternalContentKey,
        ActivationKey
    };

    private static readonly IReadOnlySet<string> FileTypeKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        ExtensionKey,
        FromCatalogKey,
        DisplayNameKey,
        IconKey,
        IconColorKey,
        IconScaleKey
    };

    private static readonly IReadOnlySet<string> TemplateKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        IdKey,
        DisplayNameKey,
        TemplateFileKey,
        DefaultKey
    };

    private static readonly IReadOnlySet<string> UtilityKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        ResourceExtensionKey,
        IconKey,
        TemplateKey,
        DockAreaKey
    };

    private static readonly IReadOnlySet<string> ConfigKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        KeyKey,
        TypeKey,
        ValuesKey,
        DefaultKey,
        DisplayNameKey,
        DescriptionKey
    };

    // The values dock-area accepts, spelled out for the error message it produces.
    private const string ValidDockAreaTokens =
        $"{WorkspaceAreaTokens.Main}, {WorkspaceAreaTokens.Bottom}, {WorkspaceAreaTokens.Side}, {NoDockAreaValue}";

    private const string DocumentTypeValue = "document";
    private const string UtilityTypeValue = "utility";
    private const string DefaultEntryPoint = "index.html";

    /// <summary>
    /// Parses a single editor manifest into an EditorContribution.
    /// </summary>
    internal static Result<EditorContribution> LoadEditor(
        string editorTomlPath,
        PackageInfo packageInfo,
        IPackageReader reader,
        IFileTypeCatalog fileTypeCatalog)
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

            var editorId = TomlTableReader.GetString(editorTable, IdKey);
            if (string.IsNullOrEmpty(editorId))
            {
                return Result.Fail($"Editor missing required '{IdKey}' field: {editorTomlPath}");
            }

            if (!EditorId.IsValidName(editorId))
            {
                return Result.Fail(
                    $"Invalid editor id '{editorId}' in manifest: {editorTomlPath}. " +
                    $"Editor ids use only lowercase letters, digits, and hyphens.");
            }

            var editorType = TomlTableReader.GetStringOrNull(editorTable, TypeKey);
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

            var displayName = TomlTableReader.GetString(editorTable, DisplayNameKey);
            if (string.IsNullOrEmpty(displayName))
            {
                return Result.Fail(
                    $"Editor missing required '{DisplayNameKey}' field in [{EditorSection}] section: {editorTomlPath}. " +
                    $"Supply a localization key or plain string for the editor's label in the Reopen-with dialog.");
            }

            // Optional; when set it is the tooltip on the Utility Panel rail button and the docked tab.
            var description = TomlTableReader.GetStringOrNull(editorTable, DescriptionKey) ?? string.Empty;

            var unknownFields = new List<string>();
            CollectUnknownManifestFields(root, editorTable, unknownFields);

            var fileTypes = new List<EditorFileType>();
            if (root.TryGetValue(FileTypesSection, out var fileTypesObject) &&
                fileTypesObject is TomlTableArray fileTypesArray)
            {
                foreach (var fileTypeTable in fileTypesArray)
                {
                    CollectUnknownFields(fileTypeTable, FileTypeKeys, FileTypesSection, unknownFields);

                    var fileTypeDisplayName = TomlTableReader.GetString(fileTypeTable, DisplayNameKey);
                    if (string.IsNullOrEmpty(fileTypeDisplayName))
                    {
                        return Result.Fail(
                            $"File type missing required '{DisplayNameKey}' field in [[{FileTypesSection}]] entry: {editorTomlPath}. " +
                            $"Supply a localization key or plain string naming the file type (e.g., the noun shown in the Reopen-with dialog).");
                    }

                    var icon = TomlTableReader.GetStringOrNull(fileTypeTable, IconKey) ?? string.Empty;
                    var iconColor = TomlTableReader.GetStringOrNull(fileTypeTable, IconColorKey) ?? string.Empty;
                    if (string.IsNullOrEmpty(icon) &&
                        !string.IsNullOrEmpty(iconColor))
                    {
                        return Result.Fail(
                            $"A [[{FileTypesSection}]] entry cannot specify '{IconColorKey}' without '{IconKey}': {editorTomlPath}");
                    }

                    var iconScale = TomlTableReader.GetDoubleOrNull(fileTypeTable, IconScaleKey);
                    if (string.IsNullOrEmpty(icon) &&
                        iconScale is not null)
                    {
                        return Result.Fail(
                            $"A [[{FileTypesSection}]] entry cannot specify '{IconScaleKey}' without '{IconKey}': {editorTomlPath}");
                    }

                    var extensionLiteral = TomlTableReader.GetStringOrNull(fileTypeTable, ExtensionKey);
                    var fromCatalogValue = TomlTableReader.GetStringOrNull(fileTypeTable, FromCatalogKey);

                    if (!string.IsNullOrEmpty(fromCatalogValue))
                    {
                        if (!string.IsNullOrEmpty(extensionLiteral))
                        {
                            return Result.Fail(
                                $"A [[{FileTypesSection}]] entry cannot specify both '{ExtensionKey}' and '{FromCatalogKey}': {editorTomlPath}");
                        }

                        if (fromCatalogValue != CatalogLanguagesValue)
                        {
                            return Result.Fail(
                                $"Unknown '{FromCatalogKey}' value '{fromCatalogValue}': {editorTomlPath}. " +
                                $"The only supported value is \"{CatalogLanguagesValue}\".");
                        }

                        foreach (var catalogExtension in fileTypeCatalog.LanguageExtensions)
                        {
                            fileTypes.Add(new EditorFileType
                            {
                                FileExtension = catalogExtension,
                                DisplayName = fileTypeDisplayName,
                                Icon = icon,
                                IconColor = iconColor,
                                IconScale = iconScale ?? 1.0
                            });
                        }
                    }
                    else
                    {
                        var extension = extensionLiteral ?? string.Empty;
                        if (!FileExtensionUtils.IsWellFormedFileExtension(extension))
                        {
                            return Result.Fail(
                                $"A [[{FileTypesSection}]] '{ExtensionKey}' value '{extension}' must be a well-formed file extension (e.g. \".txt\"): {editorTomlPath}");
                        }

                        fileTypes.Add(new EditorFileType
                        {
                            FileExtension = extension.ToLowerInvariant(),
                            DisplayName = fileTypeDisplayName,
                            Icon = icon,
                            IconColor = iconColor,
                            IconScale = iconScale ?? 1.0
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
                        Id = TomlTableReader.GetString(templateTable, IdKey),
                        DisplayName = TomlTableReader.GetString(templateTable, DisplayNameKey),
                        TemplateFile = TomlTableReader.GetString(templateTable, TemplateFileKey),
                        Default = TomlTableReader.GetBool(templateTable, DefaultKey)
                    });
                }
            }

            // An editor with external-content = true sources its content from outside the file bytes,
            // so a starter template would never be written to disk.
            if (templates.Count > 0 &&
                (TomlTableReader.GetBoolOrNull(editorTable, ExternalContentKey) ?? false))
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

            var activationResult = ParseActivation(editorTable, editorTomlPath);
            if (activationResult.IsFailure)
            {
                return Result<EditorContribution>.Fail(activationResult.FirstErrorMessage)
                    .WithErrors(activationResult);
            }
            var activation = activationResult.Value;

            var contribution = BuildContribution(root, packageInfo, editorId, displayName, description, fileTypes, templates, configDescriptors, activation, editorTable, utilityDescriptor);
            var loadedContribution = contribution with
            {
                ManifestPath = editorTomlPath,
                UnknownFields = unknownFields.AsReadOnly()
            };

            return Result<EditorContribution>.Ok(loadedContribution);
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
        string description,
        List<EditorFileType> fileTypes,
        List<DocumentTemplate> templates,
        List<ConfigDescriptor> configDescriptors,
        ActivationPolicy activation,
        TomlTable editorTable,
        UtilityDescriptor? utilityDescriptor)
    {
        var entryPoint = TomlTableReader.GetStringOrNull(editorTable, EntryPointKey) ?? DefaultEntryPoint;
        var binary = TomlTableReader.GetBoolOrNull(editorTable, BinaryKey) ?? false;
        var externalContent = TomlTableReader.GetBoolOrNull(editorTable, ExternalContentKey) ?? false;

        var options = ParseOptionsTable(root);

        return new EditorContribution
        {
            Package = packageInfo,
            Id = editorId,
            DisplayName = displayName,
            Description = description,
            FileTypes = fileTypes.AsReadOnly(),
            Templates = templates.AsReadOnly(),
            EntryPoint = entryPoint,
            Binary = binary,
            ExternalContent = externalContent,
            Activation = activation,
            Options = options,
            ConfigDescriptors = configDescriptors.AsReadOnly(),
            UtilityDescriptor = utilityDescriptor
        };
    }

    private static Result<ActivationPolicy> ParseActivation(TomlTable editorTable, string editorTomlPath)
    {
        var activationValue = TomlTableReader.GetStringOrNull(editorTable, ActivationKey);
        if (activationValue is null)
        {
            return ActivationPolicy.Required;
        }

        if (activationValue == RequiredActivationValue)
        {
            return ActivationPolicy.Required;
        }

        if (activationValue == RecommendedActivationValue)
        {
            return ActivationPolicy.Recommended;
        }

        if (activationValue == OptionalActivationValue)
        {
            return ActivationPolicy.Optional;
        }

        return Result.Fail(
            $"[{EditorSection}] '{ActivationKey}' value '{activationValue}' must be one of " +
            $"'{RequiredActivationValue}', '{RecommendedActivationValue}', or '{OptionalActivationValue}': {editorTomlPath}");
    }

    // Records every field the manifest declares that the host does not define, for each section that has
    // a fixed shape. [[file-types]] entries are collected as they are parsed instead.
    private static void CollectUnknownManifestFields(TomlTable root, TomlTable editorTable, List<string> unknownFields)
    {
        foreach (var key in root.Keys)
        {
            if (!RootKeys.Contains(key))
            {
                unknownFields.Add(key);
            }
        }

        CollectUnknownFields(editorTable, EditorKeys, EditorSection, unknownFields);

        if (root.TryGetValue(TemplatesSection, out var templatesObject) &&
            templatesObject is TomlTableArray templatesArray)
        {
            foreach (var templateTable in templatesArray)
            {
                CollectUnknownFields(templateTable, TemplateKeys, TemplatesSection, unknownFields);
            }
        }

        if (root.TryGetValue(UtilitySection, out var utilityObject) &&
            utilityObject is TomlTable utilityTable)
        {
            CollectUnknownFields(utilityTable, UtilityKeys, UtilitySection, unknownFields);
        }

        if (root.TryGetValue(ConfigSection, out var configObject) &&
            configObject is TomlTableArray configArray)
        {
            foreach (var configTable in configArray)
            {
                CollectUnknownFields(configTable, ConfigKeys, ConfigSection, unknownFields);
            }
        }
    }

    // Records each key a section does not define, so a stale or misspelled field is reported rather than
    // silently dropped. The field is ignored either way: a manifest still loads with one, because losing
    // an editor over a field the host simply does not read would cost the user more than it saves.
    private static void CollectUnknownFields(
        TomlTable table,
        IReadOnlySet<string> sectionKeys,
        string sectionName,
        List<string> unknownFields)
    {
        foreach (var key in table.Keys)
        {
            if (!sectionKeys.Contains(key))
            {
                unknownFields.Add($"{sectionName}.{key}");
            }
        }
    }

    private static Result<UtilityDescriptor> ParseUtilitySection(TomlTable utilityTable, string editorTomlPath)
    {
        var resourceExtension = TomlTableReader.GetString(utilityTable, ResourceExtensionKey);
        if (string.IsNullOrEmpty(resourceExtension))
        {
            return Result.Fail($"[{UtilitySection}] missing required '{ResourceExtensionKey}' field: {editorTomlPath}");
        }

        if (!FileExtensionUtils.IsWellFormedFileExtension(resourceExtension))
        {
            return Result.Fail(
                $"[{UtilitySection}] '{ResourceExtensionKey}' value '{resourceExtension}' must be a well-formed file extension (e.g. \".txt\"): {editorTomlPath}");
        }

        var icon = TomlTableReader.GetString(utilityTable, IconKey);
        if (string.IsNullOrEmpty(icon))
        {
            return Result.Fail($"[{UtilitySection}] missing required '{IconKey}' field: {editorTomlPath}");
        }

        var template = TomlTableReader.GetStringOrNull(utilityTable, TemplateKey) ?? string.Empty;

        var dockAreaResult = ParseDockArea(utilityTable, editorTomlPath, out var dockArea);
        if (dockAreaResult.IsFailure)
        {
            return Result<UtilityDescriptor>.Fail(dockAreaResult.FirstErrorMessage).WithErrors(dockAreaResult);
        }

        var descriptor = new UtilityDescriptor
        {
            ResourceExtension = resourceExtension.ToLowerInvariant(),
            Template = template,
            Icon = icon,
            DockArea = dockArea
        };

        return descriptor;
    }

    // Reads the dock-area key: the document area the utility's "Open as document" control sends it to, or
    // null for a utility that stays in the Utility Panel, which the manifest spells "none". The area is an
    // out parameter because a success Result cannot carry a null payload.
    private static Result ParseDockArea(TomlTable utilityTable, string editorTomlPath, out WorkspaceArea? dockArea)
    {
        dockArea = WorkspaceArea.Main;

        var dockAreaValue = TomlTableReader.GetStringOrNull(utilityTable, DockAreaKey);
        if (dockAreaValue is null)
        {
            return Result.Ok();
        }

        if (dockAreaValue == NoDockAreaValue)
        {
            dockArea = null;
            return Result.Ok();
        }

        if (!WorkspaceAreaTokens.TryParse(dockAreaValue, out var declaredArea))
        {
            return Result.Fail(
                $"[{UtilitySection}] '{DockAreaKey}' value '{dockAreaValue}' is not a recognized area " +
                $"({ValidDockAreaTokens}): {editorTomlPath}");
        }

        if (declaredArea == WorkspaceArea.Utility)
        {
            return Result.Fail(
                $"[{UtilitySection}] '{DockAreaKey}' names the Utility Panel, which holds no document tabs. " +
                $"Use '{NoDockAreaValue}' for a utility that stays in the panel: {editorTomlPath}");
        }

        dockArea = declaredArea;

        return Result.Ok();
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
            var key = TomlTableReader.GetString(configTable, KeyKey);
            if (string.IsNullOrEmpty(key))
            {
                return Result.Fail($"Config descriptor missing required '{KeyKey}' field: {editorTomlPath}");
            }

            if (!EditorId.IsValidName(key))
            {
                return Result.Fail(
                    $"Config descriptor key '{key}' must use only lowercase letters, digits, and hyphens: {editorTomlPath}");
            }

            // Reserved names are checked at package load so the error reaches the package
            // author, never a project.
            if (ContributionPropertyKeys.All.Contains(key))
            {
                return Result.Fail(
                    $"Config descriptor key '{key}' collides with a reserved contribution property: {editorTomlPath}");
            }

            if (descriptors.Any(d => string.Equals(d.Key, key, StringComparison.Ordinal)))
            {
                return Result.Fail($"Duplicate config descriptor key '{key}': {editorTomlPath}");
            }

            var typeValue = TomlTableReader.GetStringOrNull(configTable, TypeKey);
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

            var values = TomlTableReader.GetStringArray(configTable, ValuesKey);
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

            var displayName = TomlTableReader.GetString(configTable, DisplayNameKey);
            if (string.IsNullOrEmpty(displayName))
            {
                return Result.Fail($"Config descriptor '{key}' missing required '{DisplayNameKey}' field: {editorTomlPath}");
            }

            var description = TomlTableReader.GetString(configTable, DescriptionKey);

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
            if (TomlValueConverter.TryConvertStringList(array, out var items))
            {
                return items;
            }

            return value;
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

}
