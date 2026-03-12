using System.Text.Json;
using System.Text.Json.Serialization;

namespace Celbridge.Extensions;

/// <summary>
/// The type of extension editor.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExtensionEditorType>))]
public enum ExtensionEditorType
{
    /// <summary>
    /// Full WebView2 editor. Extension provides the entire UI.
    /// Communicates via IHostDocument JSON-RPC protocol.
    /// </summary>
    Custom,

    /// <summary>
    /// Monaco-based editor. Uses the built-in CodeEditorDocumentView.
    /// Can optionally configure a preview panel, customization script, or both.
    /// </summary>
    Code
}

/// <summary>
/// Preview configuration for a code extension.
/// When present, enables the split editor with a preview panel.
/// </summary>
public record ExtensionPreviewConfig
{
    /// <summary>
    /// The virtual host name for preview assets (e.g., "my-preview.celbridge").
    /// </summary>
    [JsonPropertyName("hostName")]
    public string HostName { get; init; } = string.Empty;

    /// <summary>
    /// The folder containing preview assets, relative to the extension directory.
    /// </summary>
    [JsonPropertyName("assetFolder")]
    public string AssetFolder { get; init; } = string.Empty;

    /// <summary>
    /// The URL of the preview page (e.g., "https://my-preview.celbridge/index.html").
    /// </summary>
    [JsonPropertyName("pageUrl")]
    public string PageUrl { get; init; } = string.Empty;
}

/// <summary>
/// Monaco editor options that can be configured by a code extension.
/// </summary>
public record ExtensionMonacoOptions
{
    [JsonPropertyName("scrollBeyondLastLine")]
    public bool? ScrollBeyondLastLine { get; init; }

    [JsonPropertyName("wordWrap")]
    public bool? WordWrap { get; init; }

    [JsonPropertyName("minimapEnabled")]
    public bool? MinimapEnabled { get; init; }
}

/// <summary>
/// A file type declared by an extension.
/// Declares the file extension the editor handles and an optional display name or localization key
/// shown in the Add File dialog.
/// </summary>
public record ExtensionFileType
{
    /// <summary>
    /// The file extension this editor handles (e.g., ".note").
    /// </summary>
    [JsonPropertyName("extension")]
    public string Extension { get; init; } = string.Empty;

    /// <summary>
    /// Display name or localization key shown in the Add File dialog.
    /// When omitted, falls back to the manifest name.
    /// </summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;
}

/// <summary>
/// A file template declared by an extension.
/// Templates provide starter content for new files of the extension's type.
/// </summary>
public partial record ExtensionTemplate
{
    /// <summary>
    /// Unique identifier for this template within the extension.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Display name or localization key for the template.
    /// </summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Path to the template file, relative to the extension directory.
    /// </summary>
    [JsonPropertyName("file")]
    public string File { get; init; } = string.Empty;

    /// <summary>
    /// If true, this template is used for programmatic file creation.
    /// </summary>
    [JsonPropertyName("default")]
    public bool Default { get; init; }
}

/// <summary>
/// Represents an extension editor manifest (editor.json).
/// Defines the configuration for a custom or code-based document editor.
/// </summary>
public record ExtensionManifest
{
    /// <summary>
    /// Display name of the extension.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The type of editor: "custom" (full WebView2) or "code" (Monaco-based).
    /// </summary>
    [JsonPropertyName("type")]
    public ExtensionEditorType Type { get; init; }

    /// <summary>
    /// The file types this editor handles. Each entry declares the file extension and
    /// an optional display name or localization key for the Add File dialog.
    /// </summary>
    [JsonPropertyName("file_types")]
    public IReadOnlyList<ExtensionFileType> FileTypes { get; init; } = [];

    /// <summary>
    /// Entry point for custom editors (e.g., "index.html").
    /// For code editors, this is optional.
    /// </summary>
    [JsonPropertyName("entryPoint")]
    public string? EntryPoint { get; init; }

    /// <summary>
    /// Priority for conflict resolution when multiple editors support the same extension.
    /// Higher values take precedence. Default is 0.
    /// </summary>
    [JsonPropertyName("priority")]
    public int Priority { get; init; }

    /// <summary>
    /// Optional feature flag name. When set, the editor is only available if this feature is enabled.
    /// </summary>
    [JsonPropertyName("featureFlag")]
    public string? FeatureFlag { get; init; }

    /// <summary>
    /// Optional host capabilities this extension requires (e.g., "dialog", "input").
    /// </summary>
    [JsonPropertyName("capabilities")]
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    /// <summary>
    /// Optional list of file templates provided by this extension.
    /// </summary>
    [JsonPropertyName("templates")]
    public IReadOnlyList<ExtensionTemplate> Templates { get; init; } = [];

    /// <summary>
    /// Preview configuration for code editors. When present, enables the split editor.
    /// </summary>
    [JsonPropertyName("preview")]
    public ExtensionPreviewConfig? Preview { get; init; }

    /// <summary>
    /// Path to a JS customization script for code editors, relative to the extension directory.
    /// The script is loaded after Monaco initializes and should export an activate() function.
    /// </summary>
    [JsonPropertyName("customizations")]
    public string? Customizations { get; init; }

    /// <summary>
    /// Monaco editor options for code editors.
    /// </summary>
    [JsonPropertyName("monacoOptions")]
    public ExtensionMonacoOptions? MonacoOptions { get; init; }

    /// <summary>
    /// The directory containing the extension (set during discovery, not from JSON).
    /// </summary>
    [JsonIgnore]
    public string ExtensionDirectory { get; init; } = string.Empty;

    /// <summary>
    /// A unique virtual host name for this extension's assets (set during discovery, not from JSON).
    /// </summary>
    [JsonIgnore]
    public string HostName { get; init; } = string.Empty;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Parses an extension manifest from a JSON file.
    /// Sets ExtensionDirectory and HostName based on the file location and extension name.
    /// </summary>
    public static Result<ExtensionManifest> Parse(string jsonPath)
    {
        try
        {
            var json = File.ReadAllText(jsonPath);
            var manifest = JsonSerializer.Deserialize<ExtensionManifest>(json, _jsonOptions);

            if (manifest is null)
            {
                return Result<ExtensionManifest>.Fail($"Failed to deserialize extension manifest: {jsonPath}");
            }

            if (string.IsNullOrEmpty(manifest.Name))
            {
                return Result<ExtensionManifest>.Fail($"Extension manifest is missing required 'name' field: {jsonPath}");
            }

            if (manifest.FileTypes.Count == 0)
            {
                return Result<ExtensionManifest>.Fail($"Extension manifest must declare at least one file type: {jsonPath}");
            }

            var extensionDir = Path.GetDirectoryName(jsonPath) ?? string.Empty;

            // Generate a deterministic virtual host name from the extension name
            var safeName = manifest.Name.ToLowerInvariant().Replace(' ', '-');
            var hostName = $"ext-{safeName}.celbridge";

            manifest = manifest with
            {
                ExtensionDirectory = extensionDir,
                HostName = hostName
            };

            return Result<ExtensionManifest>.Ok(manifest);
        }
        catch (JsonException ex)
        {
            return Result<ExtensionManifest>.Fail($"Invalid JSON in extension manifest: {jsonPath}")
                .WithException(ex);
        }
        catch (Exception ex)
        {
            return Result<ExtensionManifest>.Fail($"Failed to read extension manifest: {jsonPath}")
                .WithException(ex);
        }
    }
}
