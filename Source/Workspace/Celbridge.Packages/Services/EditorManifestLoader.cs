using System.Text.Json;
using Celbridge.Documents;
using Celbridge.Projects;
using Celbridge.Utilities;
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
    private const string ExtensionsFileKey = "extensions-file";
    private const string CategoryKey = "category";
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
    private const string TooltipKey = "tooltip";
    private const string LazyLoadKey = "lazy-load";

    private const string DocumentTypeValue = "document";
    private const string UtilityTypeValue = "utility";
    private const string DefaultEntryPoint = "index.html";

    /// <summary>
    /// Parses a single editor manifest into an EditorContribution.
    /// </summary>
    internal static Result<EditorContribution> LoadEditor(
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

            var editorId = TomlTableReader.GetString(editorTable, IdKey);
            if (string.IsNullOrEmpty(editorId))
            {
                return Result.Fail($"Editor missing required '{IdKey}' field: {editorTomlPath}");
            }

            if (!EditorInstanceId.IsValidName(editorId))
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
                    var fileTypeDisplayName = TomlTableReader.GetString(fileTypeTable, DisplayNameKey);
                    if (string.IsNullOrEmpty(fileTypeDisplayName))
                    {
                        return Result.Fail(
                            $"File type missing required '{DisplayNameKey}' field in [[{FileTypesSection}]] entry: {editorTomlPath}. " +
                            $"Supply a localization key or plain string naming the file type (e.g., the noun shown in the Reopen-with dialog).");
                    }

                    FileTypeCategory? category = null;
                    var categoryValue = TomlTableReader.GetStringOrNull(fileTypeTable, CategoryKey);
                    if (categoryValue is not null)
                    {
                        var categoryResult = ParseCategoryValue(categoryValue, editorTomlPath);
                        if (categoryResult.IsFailure)
                        {
                            return Result.Fail(categoryResult.FirstErrorMessage).WithErrors(categoryResult);
                        }
                        category = categoryResult.Value;
                    }

                    var extensionLiteral = TomlTableReader.GetStringOrNull(fileTypeTable, ExtensionKey);
                    var extensionsFilePath = TomlTableReader.GetStringOrNull(fileTypeTable, ExtensionsFileKey);

                    if (!string.IsNullOrEmpty(extensionsFilePath))
                    {
                        if (!string.IsNullOrEmpty(extensionLiteral))
                        {
                            return Result.Fail(
                                $"A [[{FileTypesSection}]] entry cannot specify both '{ExtensionKey}' and '{ExtensionsFileKey}': {editorTomlPath}");
                        }

                        var expandResult = ExpandExtensionsFile(packageInfo.PackageFolder, extensionsFilePath, fileTypeDisplayName, category, reader);
                        if (expandResult.IsFailure)
                        {
                            return Result.Fail($"Failed to expand '{ExtensionsFileKey}' in {editorTomlPath}")
                                .WithErrors(expandResult);
                        }

                        fileTypes.AddRange(expandResult.Value);
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
                            Category = category
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

            var contribution = BuildContribution(root, packageInfo, editorId, displayName, fileTypes, templates, configDescriptors, activation, editorTable, utilityDescriptor);

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

    // Maps a declared category value to its enum. The caller supplies null when no category is declared,
    // in which case the host classifies the extension from its catalog instead.
    private static Result<FileTypeCategory> ParseCategoryValue(string categoryValue, string editorTomlPath)
    {
        switch (categoryValue)
        {
            case "text":
                return FileTypeCategory.Text;
            case "image":
                return FileTypeCategory.Image;
            case "audio":
                return FileTypeCategory.Audio;
            case "video":
                return FileTypeCategory.Video;
            case "data":
                return FileTypeCategory.Data;
            case "document":
                return FileTypeCategory.Document;
            default:
                return Result.Fail(
                    $"[[{FileTypesSection}]] '{CategoryKey}' value '{categoryValue}' is not a recognized category " +
                    $"(text, image, audio, video, data, document): {editorTomlPath}");
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

        var tooltip = TomlTableReader.GetString(utilityTable, TooltipKey);
        if (string.IsNullOrEmpty(tooltip))
        {
            return Result.Fail($"[{UtilitySection}] missing required '{TooltipKey}' field: {editorTomlPath}");
        }

        var template = TomlTableReader.GetStringOrNull(utilityTable, TemplateKey) ?? string.Empty;
        var lazyLoad = TomlTableReader.GetBoolOrNull(utilityTable, LazyLoadKey) ?? false;

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
            var key = TomlTableReader.GetString(configTable, KeyKey);
            if (string.IsNullOrEmpty(key))
            {
                return Result.Fail($"Config descriptor missing required '{KeyKey}' field: {editorTomlPath}");
            }

            if (!EditorInstanceId.IsValidName(key))
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

    private static Result<List<EditorFileType>> ExpandExtensionsFile(
        string packageFolder,
        string relativePath,
        string displayName,
        FileTypeCategory? category,
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
                var extension = property.Name;
                if (!FileExtensionUtils.IsWellFormedFileExtension(extension))
                {
                    return Result.Fail(
                        $"Extension key '{extension}' in the extensions file must be a well-formed file extension (e.g. \".txt\"): {fullPath}");
                }

                result.Add(new EditorFileType
                {
                    FileExtension = extension.ToLowerInvariant(),
                    DisplayName = displayName,
                    Category = category
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to parse extensions file: {fullPath}").WithException(ex);
        }
    }
}
