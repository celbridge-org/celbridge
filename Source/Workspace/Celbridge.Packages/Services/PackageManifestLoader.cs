using System.Text.Json;
using Celbridge.Documents;
using Tomlyn;
using Tomlyn.Model;

namespace Celbridge.Packages;

/// <summary>
/// Parses package.toml and referenced document TOML files to produce DocumentEditorContribution objects.
/// Handles the two-level manifest structure: package identity + document contributions.
/// </summary>
public static class PackageManifestLoader
{
    private const string PackageSection = "package";
    private const string ContributesSection = "contributes";
    private const string ModSection = "mod";
    private const string DocumentSection = "document";
    private const string DocumentEditorsKey = "document_editors";
    private const string DocumentFileTypesSection = "document_file_types";
    private const string DocumentTemplatesSection = "document_templates";
    private const string CodeEditorSection = "code_editor";
    private const string CodePreviewSection = "code_preview";
    private const string OptionsSection = "options";
    private const string RequiresToolsKey = "requires_tools";

    private const string IdKey = "id";
    private const string NameKey = "name";
    private const string FeatureFlagKey = "feature_flag";
    private const string TypeKey = "type";
    private const string PriorityKey = "priority";
    private const string ExtensionKey = "extension";
    private const string ExtensionsFileKey = "extensions_file";
    private const string DisplayNameKey = "display_name";
    private const string TemplateFileKey = "template_file";
    private const string DefaultKey = "default";
    private const string EntryPointKey = "entry_point";
    private const string BinaryKey = "binary";
    private const string ExternalContentKey = "external_content";
    private const string WordWrapKey = "word_wrap";
    private const string ScrollBeyondLastLineKey = "scroll_beyond_last_line";
    private const string MinimapEnabledKey = "minimap_enabled";
    private const string CustomizationsKey = "customizations";

    private const string CodeDocumentType = "code";
    private const string GeneralPriorityValue = "general";
    private const string DefaultEntryPoint = "index.html";
    private const string PackageHostPrefix = "pkg-";
    private const string HostSuffix = ".celbridge";

    private static readonly IReadOnlyDictionary<string, string> EmptySecrets = new Dictionary<string, string>();

    /// <summary>
    /// Loads a package from a package.toml file, including all referenced document editor contributions.
    /// hostNameOverride, when non-null, replaces the default package-id-derived virtual host name.
    /// secrets, when non-empty, populates PackageInfo.Secrets for WebView injection.
    /// devToolsBlocked, when true, permanently disables DevTools on WebViews hosting this package.
    /// </summary>
    public static Result<Package> LoadPackage(
        string packageTomlPath,
        string? hostNameOverride = null,
        IReadOnlyDictionary<string, string>? secrets = null,
        bool devToolsBlocked = false)
    {
        try
        {
            var packageFolder = Path.GetFullPath(Path.GetDirectoryName(packageTomlPath) ?? string.Empty);
            var toml = File.ReadAllText(packageTomlPath);
            var parsed = Toml.Parse(toml);

            if (parsed.HasErrors)
            {
                var errors = string.Join("; ", parsed.Diagnostics.Select(d => d.ToString()));
                return Result.Fail($"TOML parse error in {packageTomlPath}: {errors}");
            }

            var root = (TomlTable)parsed.ToModel();

            if (!root.TryGetValue(PackageSection, out var packageObject) ||
                packageObject is not TomlTable packageTable)
            {
                return Result.Fail($"Missing [{PackageSection}] section: {packageTomlPath}");
            }

            var packageId = GetString(packageTable, IdKey);
            if (string.IsNullOrEmpty(packageId))
            {
                return Result.Fail($"Package missing required '{IdKey}' field: {packageTomlPath}");
            }

            if (!PackageId.IsValid(packageId))
            {
                return Result.Fail($"Package has invalid '{IdKey}' value '{packageId}': {packageTomlPath}. Package ids must use only lowercase ASCII letters, digits, dots, and hyphens, with no leading, trailing, or consecutive dots.");
            }

            var packageName = GetString(packageTable, NameKey);
            var featureFlag = GetStringOrNull(packageTable, FeatureFlagKey);

            var safeName = packageId.Replace('.', '-').ToLowerInvariant();
            var defaultHostName = $"{PackageHostPrefix}{safeName}{HostSuffix}";

            // Bundled packages may pin the virtual host name via the C#-side
            // BundledPackageDescriptor (e.g. SpreadJS licensing requires `spreadjs.celbridge`).
            // The override is deliberately not surfaced in package.toml so that non-bundled
            // packages cannot impersonate a bundled host.
            var hostName = !string.IsNullOrEmpty(hostNameOverride) ? hostNameOverride : defaultHostName;

            var requiresTools = Array.Empty<string>() as IReadOnlyList<string>;
            if (root.TryGetValue(ModSection, out var modObject) &&
                modObject is TomlTable modTable)
            {
                requiresTools = GetStringArray(modTable, RequiresToolsKey);
            }

            var packageSecrets = secrets ?? EmptySecrets;

            var packageInfo = new PackageInfo
            {
                Id = packageId,
                Name = packageName,
                FeatureFlag = featureFlag,
                PackageFolder = packageFolder,
                HostName = hostName,
                RequiresTools = requiresTools,
                Secrets = packageSecrets,
                DevToolsBlocked = devToolsBlocked
            };

            var documentPaths = new List<string>();
            if (root.TryGetValue(ContributesSection, out var contributesObject) &&
                contributesObject is TomlTable contributesTable)
            {
                if (contributesTable.TryGetValue(DocumentEditorsKey, out var documentEditorsObject) &&
                    documentEditorsObject is TomlArray documentEditorsArray)
                {
                    foreach (var documentEditor in documentEditorsArray)
                    {
                        if (documentEditor is string documentPath)
                        {
                            documentPaths.Add(documentPath);
                        }
                    }
                }
            }

            var documentEditors = new List<DocumentEditorContribution>();
            foreach (var relativePath in documentPaths)
            {
                var fullPath = Path.Combine(packageFolder, relativePath);
                var loadResult = LoadDocument(fullPath, packageInfo);
                if (loadResult.IsSuccess)
                {
                    documentEditors.Add(loadResult.Value);
                }
            }

            var package = new Package
            {
                Info = packageInfo,
                DocumentEditors = documentEditors.AsReadOnly()
            };

            return Result<Package>.Ok(package);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to load package: {packageTomlPath}").WithException(ex);
        }
    }

    /// <summary>
    /// Parses a single document TOML file into a DocumentEditorContribution.
    /// </summary>
    private static Result<DocumentEditorContribution> LoadDocument(
        string documentTomlPath,
        PackageInfo packageInfo)
    {
        try
        {
            if (!File.Exists(documentTomlPath))
            {
                return Result.Fail($"Document manifest not found: {documentTomlPath}");
            }

            var toml = File.ReadAllText(documentTomlPath);
            var parsed = Toml.Parse(toml);

            if (parsed.HasErrors)
            {
                var errors = string.Join("; ", parsed.Diagnostics.Select(d => d.ToString()));
                return Result.Fail($"TOML parse error in {documentTomlPath}: {errors}");
            }

            var root = (TomlTable)parsed.ToModel();

            if (!root.TryGetValue(DocumentSection, out var documentObject) ||
                documentObject is not TomlTable documentTable)
            {
                return Result.Fail($"Missing [{DocumentSection}] section: {documentTomlPath}");
            }

            var documentId = GetString(documentTable, IdKey);
            if (string.IsNullOrEmpty(documentId))
            {
                return Result.Fail($"Document missing required '{IdKey}' field: {documentTomlPath}");
            }

            var documentType = GetString(documentTable, TypeKey).ToLowerInvariant();
            var displayName = GetString(documentTable, DisplayNameKey);
            var priority = ParseEditorPriority(GetStringOrNull(documentTable, PriorityKey));

            var fileTypes = new List<DocumentFileType>();
            if (root.TryGetValue(DocumentFileTypesSection, out var fileTypesObject) &&
                fileTypesObject is TomlTableArray fileTypesArray)
            {
                foreach (var fileTypeTable in fileTypesArray)
                {
                    var fileTypeDisplayName = GetString(fileTypeTable, DisplayNameKey);
                    var extensionLiteral = GetStringOrNull(fileTypeTable, ExtensionKey);
                    var extensionsFilePath = GetStringOrNull(fileTypeTable, ExtensionsFileKey);

                    if (!string.IsNullOrEmpty(extensionsFilePath))
                    {
                        if (!string.IsNullOrEmpty(extensionLiteral))
                        {
                            return Result.Fail(
                                $"A [[document_file_types]] entry cannot specify both '{ExtensionKey}' and '{ExtensionsFileKey}': {documentTomlPath}");
                        }

                        var expandResult = ExpandExtensionsFile(packageInfo.PackageFolder, extensionsFilePath, fileTypeDisplayName);
                        if (expandResult.IsFailure)
                        {
                            return Result.Fail($"Failed to expand '{ExtensionsFileKey}' in {documentTomlPath}")
                                .WithErrors(expandResult);
                        }

                        fileTypes.AddRange(expandResult.Value);
                    }
                    else
                    {
                        fileTypes.Add(new DocumentFileType
                        {
                            FileExtension = extensionLiteral ?? string.Empty,
                            DisplayName = fileTypeDisplayName
                        });
                    }
                }
            }

            if (fileTypes.Count == 0)
            {
                return Result.Fail($"Document must declare at least one file type: {documentTomlPath}");
            }

            var templates = new List<DocumentTemplate>();
            if (root.TryGetValue(DocumentTemplatesSection, out var templatesObject) &&
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

            // Custom contributions have their editor id composed as "{packageId}.{documentId}" at
            // factory-construction time. Validate the composed id here so plugin authors fail fast
            // at manifest parse with a clear message, rather than hitting a DocumentEditorId
            // constructor throw later when someone tries to open a file of this type.
            if (documentType != CodeDocumentType)
            {
                var composedEditorId = $"{packageInfo.Id}.{documentId}";
                if (!DocumentEditorId.IsValid(composedEditorId))
                {
                    return Result.Fail(
                        $"Invalid document editor id '{composedEditorId}' in manifest: {documentTomlPath}. " +
                        $"Package id and document id must combine to form a DocumentEditorId using only lowercase letters, digits, dots, and hyphens.");
                }
            }

            DocumentEditorContribution contribution = documentType switch
            {
                CodeDocumentType => BuildCodeContribution(root, packageInfo, documentId, displayName, fileTypes, priority, templates),
                _ => BuildCustomContribution(root, packageInfo, documentId, displayName, fileTypes, priority, templates, documentTable)
            };

            return Result<DocumentEditorContribution>.Ok(contribution);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to load document manifest: {documentTomlPath}").WithException(ex);
        }
    }

    private static CustomDocumentEditorContribution BuildCustomContribution(
        TomlTable root,
        PackageInfo packageInfo,
        string documentId,
        string displayName,
        List<DocumentFileType> fileTypes,
        EditorPriority priority,
        List<DocumentTemplate> templates,
        TomlTable documentTable)
    {
        var entryPoint = GetStringOrNull(documentTable, EntryPointKey) ?? DefaultEntryPoint;
        var binary = GetBoolOrNull(documentTable, BinaryKey) ?? false;
        var externalContent = GetBoolOrNull(documentTable, ExternalContentKey) ?? false;

        var options = ParseOptionsTable(root);

        return new CustomDocumentEditorContribution
        {
            Package = packageInfo,
            Id = documentId,
            DisplayName = displayName,
            FileTypes = fileTypes.AsReadOnly(),
            Priority = priority,
            Templates = templates.AsReadOnly(),
            EntryPoint = entryPoint,
            Binary = binary,
            ExternalContent = externalContent,
            Options = options
        };
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

    private static CodeDocumentEditorContribution BuildCodeContribution(
        TomlTable root,
        PackageInfo packageInfo,
        string documentId,
        string displayName,
        List<DocumentFileType> fileTypes,
        EditorPriority priority,
        List<DocumentTemplate> templates)
    {
        CodeEditorConfig? codeEditor = null;
        if (root.TryGetValue(CodeEditorSection, out var codeEditorObject) &&
            codeEditorObject is TomlTable codeEditorTable)
        {
            codeEditor = new CodeEditorConfig
            {
                WordWrap = GetBoolOrNull(codeEditorTable, WordWrapKey),
                ScrollBeyondLastLine = GetBoolOrNull(codeEditorTable, ScrollBeyondLastLineKey),
                MinimapEnabled = GetBoolOrNull(codeEditorTable, MinimapEnabledKey),
                CustomizationScript = GetStringOrNull(codeEditorTable, CustomizationsKey)
            };
        }

        CodePreviewConfig? codePreview = null;
        if (root.TryGetValue(CodePreviewSection, out var codePreviewObject) &&
            codePreviewObject is TomlTable codePreviewTable)
        {
            var entryPoint = GetString(codePreviewTable, EntryPointKey);

            codePreview = new CodePreviewConfig
            {
                EntryPoint = entryPoint
            };
        }

        return new CodeDocumentEditorContribution
        {
            Package = packageInfo,
            Id = documentId,
            DisplayName = displayName,
            FileTypes = fileTypes.AsReadOnly(),
            Priority = priority,
            Templates = templates.AsReadOnly(),
            CodePreview = codePreview,
            CodeEditor = codeEditor
        };
    }

    private static Result<List<DocumentFileType>> ExpandExtensionsFile(
        string packageFolder,
        string relativePath,
        string displayName)
    {
        var fullPath = Path.Combine(packageFolder, relativePath);
        if (!File.Exists(fullPath))
        {
            return Result.Fail($"Extensions file not found: {fullPath}");
        }

        try
        {
            var json = File.ReadAllText(fullPath);
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Result.Fail($"Extensions file must be a JSON object with extension keys: {fullPath}");
            }

            var result = new List<DocumentFileType>();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                result.Add(new DocumentFileType
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

    private static EditorPriority ParseEditorPriority(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return EditorPriority.Specialized;
        }

        return value.ToLowerInvariant() switch
        {
            GeneralPriorityValue => EditorPriority.General,
            _ => EditorPriority.Specialized
        };
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
