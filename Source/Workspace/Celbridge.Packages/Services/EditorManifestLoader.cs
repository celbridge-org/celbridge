using Celbridge.Documents;
using Celbridge.Projects;
using Celbridge.Utilities;
using Celbridge.Workspace;
using System.Text.Json;
using Tomlyn;
using Tomlyn.Model;

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
    private const string UtilitySection = "utility";
    private const string ConfigSection = "config";

    private const string IdKey = "id";
    private const string TypeKey = "type";
    private const string ExtensionKey = "extension";
    private const string FromCatalogKey = "from-catalog";
    private const string DisplayNameKey = "display-name";
    private const string DefaultKey = "default";
    private const string ValuesKey = "values";
    private const string KeyKey = "key";
    private const string ExternalContentKey = "external-content";
    private const string ActivationKey = "activation";
    private const string RequiredActivationValue = "required";
    private const string RecommendedActivationValue = "recommended";
    private const string OptionalActivationValue = "optional";
    private const string ResourceExtensionKey = "resource-extension";
    private const string IconKey = "icon";
    private const string IconColorKey = "icon-color";
    private const string IconScaleKey = "icon-scale";
    private const string DockAreaKey = "dock-area";
    private const string NoDockAreaValue = "none";

    private const string CatalogLanguagesValue = "languages";

    // The manifest's keys are the kebab-case spelling of the EditorManifest property names, and every key
    // outside that set lands in an UnknownKeys bag rather than being dropped.
    private static readonly TomlSerializerOptions ManifestOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower
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

            var manifestResult = DeserializeManifest(toml, editorTomlPath);
            if (manifestResult.IsFailure)
            {
                return Result<EditorContribution>.Fail(manifestResult.FirstErrorMessage)
                    .WithErrors(manifestResult);
            }
            var manifest = manifestResult.Value;

            var editorSection = manifest.Editor;
            if (editorSection is null)
            {
                return Result.Fail($"Missing [{EditorSection}] section: {editorTomlPath}");
            }

            var editorId = editorSection.Id;
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

            var editorType = editorSection.Type;
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
            var hasUtilitySection = manifest.Utility is not null;
            var hasFileTypesSection = manifest.FileTypes.Count > 0;

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

                var utilityResult = ParseUtilitySection(manifest.Utility!, editorTomlPath);
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

            var displayName = editorSection.DisplayName;
            if (string.IsNullOrEmpty(displayName))
            {
                return Result.Fail(
                    $"Editor missing required '{DisplayNameKey}' field in [{EditorSection}] section: {editorTomlPath}. " +
                    $"Supply a localization key or plain string for the editor's label in the Reopen-with dialog.");
            }

            // Optional; when set it is the tooltip on the Utility Panel rail button and the docked tab.
            var description = editorSection.Description ?? string.Empty;

            var fileTypesResult = ParseFileTypes(manifest.FileTypes, editorTomlPath, fileTypeCatalog);
            if (fileTypesResult.IsFailure)
            {
                return Result<EditorContribution>.Fail(fileTypesResult.FirstErrorMessage)
                    .WithErrors(fileTypesResult);
            }
            var fileTypes = fileTypesResult.Value;

            if (utilityDescriptor is null &&
                fileTypes.Count == 0)
            {
                return Result.Fail($"A document editor must declare at least one file type: {editorTomlPath}");
            }

            var templates = new List<DocumentTemplate>();
            foreach (var manifestTemplate in manifest.Templates)
            {
                templates.Add(new DocumentTemplate
                {
                    Id = manifestTemplate.Id ?? string.Empty,
                    DisplayName = manifestTemplate.DisplayName ?? string.Empty,
                    TemplateFile = manifestTemplate.TemplateFile ?? string.Empty,
                    Default = manifestTemplate.Default
                });
            }

            // An editor with external-content = true sources its content from outside the file bytes,
            // so a starter template would never be written to disk.
            var externalContent = editorSection.ExternalContent ?? false;
            if (templates.Count > 0 &&
                externalContent)
            {
                return Result.Fail(
                    $"Editor manifest '{editorTomlPath}' declares both '{ExternalContentKey} = true' and [[{TemplatesSection}]]. " +
                    $"Templates cannot be used with external content.");
            }

            var descriptorsResult = ParseConfigDescriptors(manifest.Config, editorTomlPath);
            if (descriptorsResult.IsFailure)
            {
                return Result<EditorContribution>.Fail(descriptorsResult.FirstErrorMessage)
                    .WithErrors(descriptorsResult);
            }
            var configDescriptors = descriptorsResult.Value;

            var activationResult = ParseActivation(editorSection, editorTomlPath);
            if (activationResult.IsFailure)
            {
                return Result<EditorContribution>.Fail(activationResult.FirstErrorMessage)
                    .WithErrors(activationResult);
            }
            var activation = activationResult.Value;

            var contribution = new EditorContribution
            {
                Package = packageInfo,
                Id = editorId,
                DisplayName = displayName,
                Description = description,
                FileTypes = fileTypes.AsReadOnly(),
                Templates = templates.AsReadOnly(),
                EntryPoint = editorSection.EntryPoint ?? DefaultEntryPoint,
                Binary = editorSection.Binary ?? false,
                ExternalContent = externalContent,
                Activation = activation,
                Options = ParseOptions(manifest.Options),
                ConfigDescriptors = configDescriptors.AsReadOnly(),
                UtilityDescriptor = utilityDescriptor,
                ManifestPath = editorTomlPath,
                UnknownFields = CollectUnknownFields(manifest)
            };

            return Result<EditorContribution>.Ok(contribution);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to load editor manifest: {editorTomlPath}").WithException(ex);
        }
    }

    private static Result<EditorManifest> DeserializeManifest(string toml, string editorTomlPath)
    {
        try
        {
            var manifest = TomlSerializer.Deserialize<EditorManifest>(toml, ManifestOptions);
            if (manifest is null)
            {
                return Result.Fail($"Failed to deserialize editor manifest: {editorTomlPath}");
            }

            return manifest;
        }
        catch (TomlException exception)
        {
            // A shape or value error carries no diagnostic, only a message, so fall back to it.
            var detail = exception.Message;
            if (exception.Diagnostics.Count > 0)
            {
                detail = string.Join("; ", exception.Diagnostics.Select(diagnostic => diagnostic.ToString()));
            }

            return Result.Fail($"TOML error in {editorTomlPath}: {detail}");
        }
    }

    private static Result<List<EditorFileType>> ParseFileTypes(
        List<ManifestFileTypeEntry> manifestFileTypes,
        string editorTomlPath,
        IFileTypeCatalog fileTypeCatalog)
    {
        var fileTypes = new List<EditorFileType>();

        foreach (var manifestFileType in manifestFileTypes)
        {
            var displayName = manifestFileType.DisplayName;
            if (string.IsNullOrEmpty(displayName))
            {
                return Result.Fail(
                    $"File type missing required '{DisplayNameKey}' field in [[{FileTypesSection}]] entry: {editorTomlPath}. " +
                    $"Supply a localization key or plain string naming the file type (e.g., the noun shown in the Reopen-with dialog).");
            }

            var icon = manifestFileType.Icon ?? string.Empty;
            var iconColor = manifestFileType.IconColor ?? string.Empty;
            if (string.IsNullOrEmpty(icon) &&
                !string.IsNullOrEmpty(iconColor))
            {
                return Result.Fail(
                    $"A [[{FileTypesSection}]] entry cannot specify '{IconColorKey}' without '{IconKey}': {editorTomlPath}");
            }

            var iconScale = manifestFileType.IconScale;
            if (string.IsNullOrEmpty(icon) &&
                iconScale is not null)
            {
                return Result.Fail(
                    $"A [[{FileTypesSection}]] entry cannot specify '{IconScaleKey}' without '{IconKey}': {editorTomlPath}");
            }

            var fromCatalogValue = manifestFileType.FromCatalog;
            if (!string.IsNullOrEmpty(fromCatalogValue))
            {
                if (!string.IsNullOrEmpty(manifestFileType.Extension))
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
                        DisplayName = displayName,
                        Icon = icon,
                        IconColor = iconColor,
                        IconScale = iconScale ?? 1.0
                    });
                }

                continue;
            }

            var extension = manifestFileType.Extension ?? string.Empty;
            if (!FileExtensionUtils.IsWellFormedFileExtension(extension))
            {
                return Result.Fail(
                    $"A [[{FileTypesSection}]] '{ExtensionKey}' value '{extension}' must be a well-formed file extension (e.g. \".txt\"): {editorTomlPath}");
            }

            fileTypes.Add(new EditorFileType
            {
                FileExtension = extension.ToLowerInvariant(),
                DisplayName = displayName,
                Icon = icon,
                IconColor = iconColor,
                IconScale = iconScale ?? 1.0
            });
        }

        return fileTypes;
    }

    private static Result<ActivationPolicy> ParseActivation(ManifestEditorSection editorSection, string editorTomlPath)
    {
        var activationValue = editorSection.Activation;
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

    // Every field the manifest declares that the host does not define, named by its section. The keys under
    // [options] are the editor's own, so they are held as free-form data and never appear here.
    private static IReadOnlyList<string> CollectUnknownFields(EditorManifest manifest)
    {
        var unknownFields = new List<string>();

        unknownFields.AddRange(manifest.UnknownKeys.Keys);
        AddUnknownKeys(manifest.Editor?.UnknownKeys, EditorSection, unknownFields);

        foreach (var manifestFileType in manifest.FileTypes)
        {
            AddUnknownKeys(manifestFileType.UnknownKeys, FileTypesSection, unknownFields);
        }

        foreach (var manifestTemplate in manifest.Templates)
        {
            AddUnknownKeys(manifestTemplate.UnknownKeys, TemplatesSection, unknownFields);
        }

        AddUnknownKeys(manifest.Utility?.UnknownKeys, UtilitySection, unknownFields);

        foreach (var manifestConfig in manifest.Config)
        {
            AddUnknownKeys(manifestConfig.UnknownKeys, ConfigSection, unknownFields);
        }

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

    private static Result<UtilityDescriptor> ParseUtilitySection(ManifestUtilitySection utility, string editorTomlPath)
    {
        var resourceExtension = utility.ResourceExtension;
        if (string.IsNullOrEmpty(resourceExtension))
        {
            return Result.Fail($"[{UtilitySection}] missing required '{ResourceExtensionKey}' field: {editorTomlPath}");
        }

        if (!FileExtensionUtils.IsWellFormedFileExtension(resourceExtension))
        {
            return Result.Fail(
                $"[{UtilitySection}] '{ResourceExtensionKey}' value '{resourceExtension}' must be a well-formed file extension (e.g. \".txt\"): {editorTomlPath}");
        }

        var icon = utility.Icon;
        if (string.IsNullOrEmpty(icon))
        {
            return Result.Fail($"[{UtilitySection}] missing required '{IconKey}' field: {editorTomlPath}");
        }

        var template = utility.Template ?? string.Empty;

        var dockAreaResult = ParseDockArea(utility, editorTomlPath, out var dockArea);
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
    private static Result ParseDockArea(ManifestUtilitySection utility, string editorTomlPath, out WorkspaceArea? dockArea)
    {
        dockArea = WorkspaceArea.Main;

        var dockAreaValue = utility.DockArea;
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

    private static Result<List<ConfigDescriptor>> ParseConfigDescriptors(
        List<ManifestConfigEntry> manifestConfigs,
        string editorTomlPath)
    {
        var descriptors = new List<ConfigDescriptor>();

        foreach (var manifestConfig in manifestConfigs)
        {
            var key = manifestConfig.Key;
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

            var typeValue = manifestConfig.Type;
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

            var values = manifestConfig.Values;
            if (descriptorType == ConfigValueType.Enum)
            {
                if (values is null ||
                    values.Count == 0)
                {
                    return Result.Fail(
                        $"Config descriptor '{key}' of type \"enum\" requires a non-empty '{ValuesKey}' list: {editorTomlPath}");
                }
            }
            else if (values is not null)
            {
                return Result.Fail(
                    $"Config descriptor '{key}' declares '{ValuesKey}' but is not of type \"enum\": {editorTomlPath}");
            }

            var displayName = manifestConfig.DisplayName;
            if (string.IsNullOrEmpty(displayName))
            {
                return Result.Fail($"Config descriptor '{key}' missing required '{DisplayNameKey}' field: {editorTomlPath}");
            }

            var descriptor = new ConfigDescriptor
            {
                Key = key,
                Type = descriptorType.Value,
                Values = values?.AsReadOnly() ?? (IReadOnlyList<string>)Array.Empty<string>(),
                DisplayName = displayName,
                Description = manifestConfig.Description ?? string.Empty
            };

            if (manifestConfig.Default is not null)
            {
                var rawDefault = NormalizeTomlValue(manifestConfig.Default);
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

    private static IReadOnlyDictionary<string, string> ParseOptions(Dictionary<string, object?> options)
    {
        var result = new Dictionary<string, string>();
        foreach (var entry in options)
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
