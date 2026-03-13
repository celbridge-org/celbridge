using Tomlyn;
using Tomlyn.Model;

namespace Celbridge.Extensions;

/// <summary>
/// Parses extension.toml and referenced document TOML files to produce ExtensionManifest objects.
/// Handles the two-level manifest structure: extension identity + document contributions.
/// </summary>
public static class ManifestLoader
{
    /// <summary>
    /// Loads all document manifests from an extension.toml file and its referenced document files.
    /// </summary>
    public static Result<IReadOnlyList<ExtensionManifest>> LoadExtension(string extensionTomlPath)
    {
        try
        {
            var extensionDir = Path.GetDirectoryName(extensionTomlPath) ?? string.Empty;
            var toml = File.ReadAllText(extensionTomlPath);
            var parsed = Toml.Parse(toml);

            if (parsed.HasErrors)
            {
                var errors = string.Join("; ", parsed.Diagnostics.Select(d => d.ToString()));
                return Result<IReadOnlyList<ExtensionManifest>>.Fail($"TOML parse error in {extensionTomlPath}: {errors}");
            }

            var root = (TomlTable)parsed.ToModel();

            // Parse [extension] section
            if (!root.TryGetValue("extension", out var extObj) || extObj is not TomlTable extTable)
            {
                return Result<IReadOnlyList<ExtensionManifest>>.Fail(
                    $"Missing [extension] section: {extensionTomlPath}");
            }

            var extId = GetString(extTable, "id");
            var extName = GetString(extTable, "name");
            var extFeatureFlag = GetStringOrNull(extTable, "feature_flag");

            if (string.IsNullOrEmpty(extId))
            {
                return Result<IReadOnlyList<ExtensionManifest>>.Fail(
                    $"Extension missing required 'id' field: {extensionTomlPath}");
            }

            // Generate host name from extension id
            var safeName = extId.Replace('.', '-').ToLowerInvariant();
            var hostName = $"ext-{safeName}.celbridge";

            // Parse [contributes] section
            var documentPaths = new List<string>();
            if (root.TryGetValue("contributes", out var contribObj) && contribObj is TomlTable contribTable)
            {
                if (contribTable.TryGetValue("documents", out var docsObj) && docsObj is TomlArray docsArray)
                {
                    foreach (var doc in docsArray)
                    {
                        if (doc is string docPath)
                        {
                            documentPaths.Add(docPath);
                        }
                    }
                }
            }

            // Parse each referenced document TOML file
            var manifests = new List<ExtensionManifest>();
            foreach (var docRelativePath in documentPaths)
            {
                var docFullPath = Path.Combine(extensionDir, docRelativePath);
                var docResult = LoadDocument(docFullPath, extName, extFeatureFlag, extensionDir, hostName);
                if (docResult.IsSuccess)
                {
                    manifests.Add(docResult.Value);
                }
            }

            return Result<IReadOnlyList<ExtensionManifest>>.Ok(manifests.AsReadOnly());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<ExtensionManifest>>.Fail(
                $"Failed to load extension: {extensionTomlPath}").WithException(ex);
        }
    }

    /// <summary>
    /// Parses a single document TOML file into an ExtensionManifest.
    /// </summary>
    private static Result<ExtensionManifest> LoadDocument(
        string documentTomlPath,
        string extensionName,
        string? extensionFeatureFlag,
        string extensionDir,
        string hostName)
    {
        try
        {
            if (!File.Exists(documentTomlPath))
            {
                return Result<ExtensionManifest>.Fail(
                    $"Document manifest not found: {documentTomlPath}");
            }

            var toml = File.ReadAllText(documentTomlPath);
            var parsed = Toml.Parse(toml);

            if (parsed.HasErrors)
            {
                var errors = string.Join("; ", parsed.Diagnostics.Select(d => d.ToString()));
                return Result<ExtensionManifest>.Fail(
                    $"TOML parse error in {documentTomlPath}: {errors}");
            }

            var root = (TomlTable)parsed.ToModel();

            // Parse [document] section
            if (!root.TryGetValue("document", out var docObj) || docObj is not TomlTable docTable)
            {
                return Result<ExtensionManifest>.Fail(
                    $"Missing [document] section: {documentTomlPath}");
            }

            var docId = GetString(docTable, "id");
            var docType = ParseEditorType(GetString(docTable, "type"));
            var entryPoint = GetStringOrNull(docTable, "entry_point");
            var priority = GetInt(docTable, "priority");
            var capabilities = GetStringArray(docTable, "capabilities");

            if (string.IsNullOrEmpty(docId))
            {
                return Result<ExtensionManifest>.Fail(
                    $"Document missing required 'id' field: {documentTomlPath}");
            }

            // Parse [[document_file_types]]
            var fileTypes = new List<DocumentFileType>();
            if (root.TryGetValue("document_file_types", out var ftObj) && ftObj is TomlTableArray ftArray)
            {
                foreach (var ft in ftArray)
                {
                    fileTypes.Add(new DocumentFileType
                    {
                        Extension = GetString(ft, "extension"),
                        DisplayName = GetString(ft, "display_name")
                    });
                }
            }

            if (fileTypes.Count == 0)
            {
                return Result<ExtensionManifest>.Fail(
                    $"Document must declare at least one file type: {documentTomlPath}");
            }

            // Parse [[document_templates]]
            var templates = new List<DocumentTemplate>();
            if (root.TryGetValue("document_templates", out var tmplObj) && tmplObj is TomlTableArray tmplArray)
            {
                foreach (var tmpl in tmplArray)
                {
                    templates.Add(new DocumentTemplate
                    {
                        Id = GetString(tmpl, "id"),
                        DisplayName = GetString(tmpl, "display_name"),
                        File = GetString(tmpl, "file"),
                        Default = GetBool(tmpl, "default")
                    });
                }
            }

            // Parse [code_editor]
            CodeEditorConfig? codeEditor = null;
            if (root.TryGetValue("code_editor", out var ceObj) && ceObj is TomlTable ceTable)
            {
                codeEditor = new CodeEditorConfig
                {
                    WordWrap = GetBoolOrNull(ceTable, "word_wrap"),
                    ScrollBeyondLastLine = GetBoolOrNull(ceTable, "scroll_beyond_last_line"),
                    MinimapEnabled = GetBoolOrNull(ceTable, "minimap_enabled"),
                    Customizations = GetStringOrNull(ceTable, "customizations")
                };
            }

            // Parse [code_preview]
            CodePreviewConfig? codePreview = null;
            if (root.TryGetValue("code_preview", out var cpObj) && cpObj is TomlTable cpTable)
            {
                var assetFolder = GetString(cpTable, "asset_folder");
                var pageUrl = GetString(cpTable, "page_url");

                // Auto-generate preview host name from extension host name
                var previewHostName = hostName.Replace(".celbridge", "-preview.celbridge");

                codePreview = new CodePreviewConfig
                {
                    AssetFolder = assetFolder,
                    PageUrl = $"https://{previewHostName}/{pageUrl}",
                    HostName = previewHostName
                };
            }

            var manifest = new ExtensionManifest
            {
                Id = docId,
                Name = extensionName,
                Type = docType,
                FileTypes = fileTypes.AsReadOnly(),
                EntryPoint = entryPoint,
                Priority = priority,
                FeatureFlag = extensionFeatureFlag,
                Capabilities = capabilities,
                Templates = templates.AsReadOnly(),
                CodePreview = codePreview,
                CodeEditor = codeEditor,
                ExtensionDirectory = extensionDir,
                HostName = hostName
            };

            return Result<ExtensionManifest>.Ok(manifest);
        }
        catch (Exception ex)
        {
            return Result<ExtensionManifest>.Fail(
                $"Failed to load document manifest: {documentTomlPath}").WithException(ex);
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

    private static int GetInt(TomlTable table, string key)
    {
        if (table.TryGetValue(key, out var value))
        {
            return value switch
            {
                long l => (int)l,
                int i => i,
                _ => 0
            };
        }
        return 0;
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
