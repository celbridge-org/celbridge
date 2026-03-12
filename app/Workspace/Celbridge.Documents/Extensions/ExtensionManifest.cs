using System.Text.Json;
using System.Text.Json.Serialization;

namespace Celbridge.Documents.Extensions;

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
    /// File extensions this editor handles (e.g., [".myext", ".other"]).
    /// </summary>
    [JsonPropertyName("extensions")]
    public IReadOnlyList<string> Extensions { get; init; } = [];

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

            if (manifest.Extensions.Count == 0)
            {
                return Result<ExtensionManifest>.Fail($"Extension manifest must specify at least one file extension: {jsonPath}");
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
