using Celbridge.Documents;
using Tomlyn;
using Tomlyn.Model;

namespace Celbridge.Extensions;

/// <summary>
/// Parses extension.toml and referenced document TOML files to produce DocumentContribution objects.
/// Handles the two-level manifest structure: extension identity + document contributions.
/// </summary>
public static class ManifestLoader
{
    private const string ExtensionSection = "extension";
    private const string ContributesSection = "contributes";
    private const string DocumentSection = "document";
    private const string DocumentEditorsKey = "document_editors";
    private const string DocumentFileTypesSection = "document_file_types";
    private const string DocumentTemplatesSection = "document_templates";
    private const string CodeEditorSection = "code_editor";
    private const string CodePreviewSection = "code_preview";

    private const string IdKey = "id";
    private const string NameKey = "name";
    private const string FeatureFlagKey = "feature_flag";
    private const string TypeKey = "type";
    private const string PriorityKey = "priority";
    private const string ExtensionKey = "extension";
    private const string DisplayNameKey = "display_name";
    private const string TemplateFileKey = "template_file";
    private const string DefaultKey = "default";
    private const string EntryPointKey = "entry_point";
    private const string WordWrapKey = "word_wrap";
    private const string ScrollBeyondLastLineKey = "scroll_beyond_last_line";
    private const string MinimapEnabledKey = "minimap_enabled";
    private const string CustomizationsKey = "customizations";

    private const string CodeDocumentType = "code";
    private const string GeneralPriorityValue = "general";
    private const string DefaultEntryPoint = "index.html";
    private const string ExtensionHostPrefix = "ext-";
    private const string HostSuffix = ".celbridge";

    /// <summary>
    /// Loads an extension from an extension.toml file, including all referenced document editor contributions.
    /// </summary>
    public static Result<Extension> LoadExtension(string extensionTomlPath)
    {
        try
        {
            var extensionFolder = Path.GetFullPath(Path.GetDirectoryName(extensionTomlPath) ?? string.Empty);
            var toml = File.ReadAllText(extensionTomlPath);
            var parsed = Toml.Parse(toml);

            if (parsed.HasErrors)
            {
                var errors = string.Join("; ", parsed.Diagnostics.Select(d => d.ToString()));
                return Result.Fail($"TOML parse error in {extensionTomlPath}: {errors}");
            }

            var root = (TomlTable)parsed.ToModel();

            if (!root.TryGetValue(ExtensionSection, out var extensionObject) ||
                extensionObject is not TomlTable extensionTable)
            {
                return Result.Fail($"Missing [{ExtensionSection}] section: {extensionTomlPath}");
            }

            var extensionId = GetString(extensionTable, IdKey);
            if (string.IsNullOrEmpty(extensionId))
            {
                return Result.Fail($"Extension missing required '{IdKey}' field: {extensionTomlPath}");
            }

            var extensionName = GetString(extensionTable, NameKey);
            var featureFlag = GetStringOrNull(extensionTable, FeatureFlagKey);

            var safeName = extensionId.Replace('.', '-').ToLowerInvariant();
            var hostName = $"{ExtensionHostPrefix}{safeName}{HostSuffix}";

            var extensionInfo = new ExtensionInfo
            {
                Id = extensionId,
                Name = extensionName,
                FeatureFlag = featureFlag,
                ExtensionFolder = extensionFolder,
                HostName = hostName
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

            var documentEditors = new List<DocumentContribution>();
            foreach (var relativePath in documentPaths)
            {
                var fullPath = Path.Combine(extensionFolder, relativePath);
                var loadResult = LoadDocument(fullPath, extensionInfo);
                if (loadResult.IsSuccess)
                {
                    documentEditors.Add(loadResult.Value);
                }
            }

            var extension = new Extension
            {
                Info = extensionInfo,
                DocumentEditors = documentEditors.AsReadOnly()
            };

            return Result<Extension>.Ok(extension);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to load extension: {extensionTomlPath}").WithException(ex);
        }
    }

    /// <summary>
    /// Parses a single document TOML file into a DocumentContribution.
    /// </summary>
    private static Result<DocumentContribution> LoadDocument(
        string documentTomlPath,
        ExtensionInfo extensionInfo)
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
            var priority = ParseEditorPriority(GetStringOrNull(documentTable, PriorityKey));

            var fileTypes = new List<DocumentFileType>();
            if (root.TryGetValue(DocumentFileTypesSection, out var fileTypesObject) &&
                fileTypesObject is TomlTableArray fileTypesArray)
            {
                foreach (var fileTypeTable in fileTypesArray)
                {
                    fileTypes.Add(new DocumentFileType
                    {
                        FileExtension = GetString(fileTypeTable, ExtensionKey),
                        DisplayName = GetString(fileTypeTable, DisplayNameKey)
                    });
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

            DocumentContribution contribution = documentType switch
            {
                CodeDocumentType => BuildCodeContribution(root, extensionInfo, documentId, fileTypes, priority, templates),
                _ => BuildCustomContribution(root, extensionInfo, documentId, fileTypes, priority, templates, documentTable)
            };

            return Result<DocumentContribution>.Ok(contribution);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to load document manifest: {documentTomlPath}").WithException(ex);
        }
    }

    private static CustomDocumentContribution BuildCustomContribution(
        TomlTable root,
        ExtensionInfo extensionInfo,
        string documentId,
        List<DocumentFileType> fileTypes,
        EditorPriority priority,
        List<DocumentTemplate> templates,
        TomlTable documentTable)
    {
        var entryPoint = GetStringOrNull(documentTable, EntryPointKey) ?? DefaultEntryPoint;

        return new CustomDocumentContribution
        {
            Extension = extensionInfo,
            Id = documentId,
            FileTypes = fileTypes.AsReadOnly(),
            Priority = priority,
            Templates = templates.AsReadOnly(),
            EntryPoint = entryPoint
        };
    }

    private static CodeDocumentContribution BuildCodeContribution(
        TomlTable root,
        ExtensionInfo extensionInfo,
        string documentId,
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

        return new CodeDocumentContribution
        {
            Extension = extensionInfo,
            Id = documentId,
            FileTypes = fileTypes.AsReadOnly(),
            Priority = priority,
            Templates = templates.AsReadOnly(),
            CodePreview = codePreview,
            CodeEditor = codeEditor
        };
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
}
