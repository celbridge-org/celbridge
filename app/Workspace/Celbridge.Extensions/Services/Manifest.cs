namespace Celbridge.Extensions;

/// <summary>
/// The type of extension editor.
/// </summary>
public enum EditorType
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
public record PreviewConfig
{
    /// <summary>
    /// The folder containing preview assets, relative to the extension directory.
    /// </summary>
    public string AssetFolder { get; init; } = string.Empty;

    /// <summary>
    /// The page to load in the preview panel (e.g., "index.html").
    /// </summary>
    public string PageUrl { get; init; } = string.Empty;

    /// <summary>
    /// The virtual host name for preview assets (auto-generated during loading).
    /// </summary>
    public string HostName { get; init; } = string.Empty;
}

/// <summary>
/// Monaco editor configuration for code extensions.
/// Combines editor options and customization script path.
/// </summary>
public record MonacoConfig
{
    public bool? ScrollBeyondLastLine { get; init; }
    public bool? WordWrap { get; init; }
    public bool? MinimapEnabled { get; init; }

    /// <summary>
    /// Path to a JS customization script, relative to the extension directory.
    /// The script is loaded after Monaco initializes and should export an activate() function.
    /// </summary>
    public string? Customizations { get; init; }
}

/// <summary>
/// A file type declared by an extension.
/// Declares the file extension the editor handles and an optional display name or localization key
/// shown in the Add File dialog.
/// </summary>
public record FileType
{
    /// <summary>
    /// The file extension this editor handles (e.g., ".note").
    /// </summary>
    public string Extension { get; init; } = string.Empty;

    /// <summary>
    /// Display name or localization key shown in the Add File dialog.
    /// When omitted, falls back to the extension name.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;
}

/// <summary>
/// A file template declared by an extension.
/// Templates provide starter content for new files of the extension's type.
/// </summary>
public partial record Template
{
    /// <summary>
    /// Unique identifier for this template within the extension.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Display name or localization key for the template.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Path to the template file, relative to the extension directory.
    /// </summary>
    public string File { get; init; } = string.Empty;

    /// <summary>
    /// If true, this template is used for programmatic file creation.
    /// </summary>
    public bool Default { get; init; }
}

/// <summary>
/// Represents a document editor contribution parsed from a TOML document manifest.
/// Each extension can contribute one or more document editors via its extension.toml.
/// </summary>
public partial record Manifest
{
    /// <summary>
    /// Unique identifier for this document contribution (e.g., "note-document").
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Display name of the parent extension (from extension.toml).
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The type of editor: "custom" (full WebView2) or "code" (Monaco-based).
    /// </summary>
    public EditorType Type { get; init; }

    /// <summary>
    /// The file types this editor handles. Each entry declares the file extension and
    /// an optional display name or localization key for the Add File dialog.
    /// </summary>
    public IReadOnlyList<FileType> FileTypes { get; init; } = [];

    /// <summary>
    /// Entry point for custom editors (e.g., "index.html").
    /// For code editors, this is optional.
    /// </summary>
    public string? EntryPoint { get; init; }

    /// <summary>
    /// Priority for conflict resolution when multiple editors support the same extension.
    /// Higher values take precedence. Default is 0.
    /// </summary>
    public int Priority { get; init; }

    /// <summary>
    /// Optional feature flag from the parent extension.
    /// When set, all document contributions are disabled if this feature is off.
    /// Features default to enabled; only an explicit "false" disables them.
    /// </summary>
    public string? FeatureFlag { get; init; }

    /// <summary>
    /// Optional host capabilities this extension requires (e.g., "dialog", "input").
    /// </summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    /// <summary>
    /// Optional list of file templates provided by this extension.
    /// </summary>
    public IReadOnlyList<Template> Templates { get; init; } = [];

    /// <summary>
    /// Preview configuration for code editors. When present, enables the split editor.
    /// </summary>
    public PreviewConfig? Preview { get; init; }

    /// <summary>
    /// Monaco editor configuration for code extensions.
    /// </summary>
    public MonacoConfig? Monaco { get; init; }

    /// <summary>
    /// The directory containing the extension (set during loading, not from TOML).
    /// </summary>
    public string ExtensionDirectory { get; init; } = string.Empty;

    /// <summary>
    /// A unique virtual host name for this extension's assets (set during loading, not from TOML).
    /// </summary>
    public string HostName { get; init; } = string.Empty;
}
