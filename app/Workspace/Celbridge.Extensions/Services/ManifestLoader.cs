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
    /// <summary>
    /// Loads all document contributions from an extension.toml file and its referenced document files.
    /// </summary>
    public static Result<IReadOnlyList<DocumentContribution>> LoadExtension(string extensionTomlPath)
    {
        try
        {
            var extensionDirectory = Path.GetDirectoryName(extensionTomlPath) ?? string.Empty;
            var toml = File.ReadAllText(extensionTomlPath);
            var parsed = Toml.Parse(toml);

            if (parsed.HasErrors)
            {
                var errors = string.Join("; ", parsed.Diagnostics.Select(d => d.ToString()));
                return Result.Fail($"TOML parse error in {extensionTomlPath}: {errors}");
            }

            var root = (TomlTable)parsed.ToModel();

            // [extension]
            if (!root.TryGetValue("extension", out var extensionObject) ||
                extensionObject is not TomlTable extensionTable)
            {
                return Result.Fail($"Missing [extension] section: {extensionTomlPath}");
            }

            var extensionId = GetString(extensionTable, "id");
            if (string.IsNullOrEmpty(extensionId))
            {
                return Result.Fail($"Extension missing required 'id' field: {extensionTomlPath}");
            }

            var extensionName = GetString(extensionTable, "name");
            var featureFlag = GetStringOrNull(extensionTable, "feature_flag");
            var capabilities = GetStringArray(extensionTable, "capabilities");

            var safeName = extensionId.Replace('.', '-').ToLowerInvariant();
            var hostName = $"ext-{safeName}.celbridge";

            var extensionInfo = new ExtensionInfo
            {
                Id = extensionId,
                Name = extensionName,
                FeatureFlag = featureFlag,
                Capabilities = capabilities,
                ExtensionDirectory = extensionDirectory,
                HostName = hostName
            };

            // [contributes]
            var documentPaths = new List<string>();
            if (root.TryGetValue("contributes", out var contributesObject) &&
                contributesObject is TomlTable contributesTable)
            {
                if (contributesTable.TryGetValue("documents", out var documentsObject) &&
                    documentsObject is TomlArray documentsArray)
                {
                    foreach (var document in documentsArray)
                    {
                        if (document is string documentPath)
                        {
                            documentPaths.Add(documentPath);
                        }
                    }
                }
            }

            // Load each referenced document TOML file
            var contributions = new List<DocumentContribution>();
            foreach (var relativePath in documentPaths)
            {
                var fullPath = Path.Combine(extensionDirectory, relativePath);
                var loadResult = LoadDocument(fullPath, extensionInfo, hostName);
                if (loadResult.IsSuccess)
                {
                    contributions.Add(loadResult.Value);
                }
            }

            return Result<IReadOnlyList<DocumentContribution>>.Ok(contributions.AsReadOnly());
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
        ExtensionInfo extensionInfo,
        string hostName)
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

            // [document]
            if (!root.TryGetValue("document", out var documentObject) ||
                documentObject is not TomlTable documentTable)
            {
                return Result.Fail($"Missing [document] section: {documentTomlPath}");
            }

            var documentId = GetString(documentTable, "id");
            if (string.IsNullOrEmpty(documentId))
            {
                return Result.Fail($"Document missing required 'id' field: {documentTomlPath}");
            }

            var documentType = ParseEditorType(GetString(documentTable, "type"));
            var entryPoint = GetStringOrNull(documentTable, "entry_point");
            var priority = ParseEditorPriority(GetStringOrNull(documentTable, "priority"));

            // [[document_file_types]]
            var fileTypes = new List<DocumentFileType>();
            if (root.TryGetValue("document_file_types", out var fileTypesObject) &&
                fileTypesObject is TomlTableArray fileTypesArray)
            {
                foreach (var fileTypeTable in fileTypesArray)
                {
                    fileTypes.Add(new DocumentFileType
                    {
                        Extension = GetString(fileTypeTable, "extension"),
                        DisplayName = GetString(fileTypeTable, "display_name")
                    });
                }
            }

            if (fileTypes.Count == 0)
            {
                return Result.Fail($"Document must declare at least one file type: {documentTomlPath}");
            }

            // [[document_templates]]
            var templates = new List<DocumentTemplate>();
            if (root.TryGetValue("document_templates", out var templatesObject) &&
                templatesObject is TomlTableArray templatesArray)
            {
                foreach (var templateTable in templatesArray)
                {
                    templates.Add(new DocumentTemplate
                    {
                        Id = GetString(templateTable, "id"),
                        DisplayName = GetString(templateTable, "display_name"),
                        File = GetString(templateTable, "file"),
                        Default = GetBool(templateTable, "default")
                    });
                }
            }

            // [code_editor]
            CodeEditorConfig? codeEditor = null;
            if (root.TryGetValue("code_editor", out var codeEditorObject) &&
                codeEditorObject is TomlTable codeEditorTable)
            {
                codeEditor = new CodeEditorConfig
                {
                    WordWrap = GetBoolOrNull(codeEditorTable, "word_wrap"),
                    ScrollBeyondLastLine = GetBoolOrNull(codeEditorTable, "scroll_beyond_last_line"),
                    MinimapEnabled = GetBoolOrNull(codeEditorTable, "minimap_enabled"),
                    Customizations = GetStringOrNull(codeEditorTable, "customizations")
                };
            }

            // [code_preview]
            CodePreviewConfig? codePreview = null;
            if (root.TryGetValue("code_preview", out var codePreviewObject) &&
                codePreviewObject is TomlTable codePreviewTable)
            {
                var assetFolder = GetString(codePreviewTable, "asset_folder");
                var pageUrl = GetString(codePreviewTable, "page_url");
                var previewHostName = hostName.Replace(".celbridge", "-preview.celbridge");

                codePreview = new CodePreviewConfig
                {
                    AssetFolder = assetFolder,
                    PageUrl = $"https://{previewHostName}/{pageUrl}",
                    HostName = previewHostName
                };
            }

            return new DocumentContribution
            {
                Extension = extensionInfo,
                Id = documentId,
                Type = documentType,
                FileTypes = fileTypes.AsReadOnly(),
                EntryPoint = entryPoint,
                Priority = priority,
                Templates = templates.AsReadOnly(),
                CodePreview = codePreview,
                CodeEditor = codeEditor
            };
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to load document manifest: {documentTomlPath}").WithException(ex);
        }
    }

    private static DocumentEditorType ParseEditorType(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "code" => DocumentEditorType.Code,
            _ => DocumentEditorType.Custom
        };
    }

    private static EditorPriority ParseEditorPriority(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return EditorPriority.Default;
        }

        return value.ToLowerInvariant() switch
        {
            "option" => EditorPriority.Option,
            _ => EditorPriority.Default
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
        if (table.TryGetValue(key, out var value) && value is TomlArray array)
        {
            return array
                .OfType<string>()
                .ToList()
                .AsReadOnly();
        }

        return [];
    }
}
