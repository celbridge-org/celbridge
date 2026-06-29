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
    private const string PermissionsSection = "permissions";
    private const string DocumentSection = "document";
    private const string DocumentEditorsKey = "document_editors";
    private const string DocumentFileTypesSection = "document_file_types";
    private const string DocumentTemplatesSection = "document_templates";
    private const string CodeEditorSection = "code_editor";
    private const string CodePreviewSection = "code_preview";
    private const string OptionsSection = "options";
    private const string ToolsKey = "tools";

    private const string IdKey = "id";
    private const string NameKey = "name";
    private const string TitleKey = "title";
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

    private static readonly IReadOnlyDictionary<string, string> EmptySecrets = new Dictionary<string, string>();

    /// <summary>
    /// Loads a package from a package.toml file, including all referenced document editor contributions.
    /// secrets populates PackageInfo.Secrets for WebView injection.
    /// devToolsBlocked permanently disables DevTools on the package's WebViews.
    /// origin tags PackageInfo so downstream read sites pick the right IO path.
    /// reader is the file-read primitive; null selects DirectPackageReader (direct disk), the legacy
    /// behaviour for callers (tests, bundled discovery) with no IResourceFileSystem to route through.
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
            var featureFlag = GetStringOrNull(packageTable, FeatureFlagKey);

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
                FeatureFlag = featureFlag,
                PackageFolder = packageFolder,
                PermittedTools = permittedTools,
                Secrets = packageSecrets,
                DevToolsBlocked = devToolsBlocked,
                Origin = origin
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
                var loadResult = LoadDocument(fullPath, packageInfo, reader);
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
        PackageInfo packageInfo,
        IPackageReader reader)
    {
        try
        {
            if (!reader.Exists(documentTomlPath))
            {
                return Result.Fail($"Document manifest not found: {documentTomlPath}");
            }

            var readResult = reader.ReadAllText(documentTomlPath);
            if (readResult.IsFailure)
            {
                return Result.Fail($"Failed to read document manifest: {documentTomlPath}")
                    .WithErrors(readResult);
            }
            var toml = readResult.Value;
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
            if (string.IsNullOrEmpty(displayName))
            {
                return Result.Fail(
                    $"Document missing required '{DisplayNameKey}' field in [{DocumentSection}] section: {documentTomlPath}. " +
                    $"Supply a localization key or plain string for the editor's label in the Reopen-with dialog.");
            }
            var priority = ParseEditorPriority(GetStringOrNull(documentTable, PriorityKey));

            var fileTypes = new List<DocumentFileType>();
            if (root.TryGetValue(DocumentFileTypesSection, out var fileTypesObject) &&
                fileTypesObject is TomlTableArray fileTypesArray)
            {
                foreach (var fileTypeTable in fileTypesArray)
                {
                    var fileTypeDisplayName = GetString(fileTypeTable, DisplayNameKey);
                    if (string.IsNullOrEmpty(fileTypeDisplayName))
                    {
                        return Result.Fail(
                            $"File type missing required '{DisplayNameKey}' field in [[{DocumentFileTypesSection}]] entry: {documentTomlPath}. " +
                            $"Supply a localization key or plain string naming the file type (e.g., the noun shown in the Reopen-with dialog).");
                    }

                    var extensionLiteral = GetStringOrNull(fileTypeTable, ExtensionKey);
                    var extensionsFilePath = GetStringOrNull(fileTypeTable, ExtensionsFileKey);

                    if (!string.IsNullOrEmpty(extensionsFilePath))
                    {
                        if (!string.IsNullOrEmpty(extensionLiteral))
                        {
                            return Result.Fail(
                                $"A [[document_file_types]] entry cannot specify both '{ExtensionKey}' and '{ExtensionsFileKey}': {documentTomlPath}");
                        }

                        var expandResult = ExpandExtensionsFile(packageInfo.PackageFolder, extensionsFilePath, fileTypeDisplayName, reader);
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

            // An editor with external_content = true sources its content from outside the file bytes,
            // so a starter template would never be written to disk. The combination is meaningless,
            // and accepting it silently hides an authoring mistake.
            if (documentType != CodeDocumentType &&
                templates.Count > 0 &&
                (GetBoolOrNull(documentTable, ExternalContentKey) ?? false))
            {
                return Result.Fail(
                    $"Document manifest '{documentTomlPath}' declares both '{ExternalContentKey} = true' and '{DocumentTemplatesSection}'. " +
                    $"Templates cannot be used with external content.");
            }

            // Custom contributions have their editor id composed as "{packageName}.{documentId}" at
            // factory-construction time. Validate the composed id here so plugin authors fail fast
            // at manifest parse with a clear message, rather than hitting a DocumentEditorId
            // constructor throw later when someone tries to open a file of this type.
            if (documentType != CodeDocumentType)
            {
                var composedEditorId = $"{packageInfo.Name}.{documentId}";
                if (!DocumentEditorId.IsValid(composedEditorId))
                {
                    return Result.Fail(
                        $"Invalid document editor id '{composedEditorId}' in manifest: {documentTomlPath}. " +
                        $"Package name and document id must combine to form a DocumentEditorId using only lowercase letters, digits, dots, and hyphens.");
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
