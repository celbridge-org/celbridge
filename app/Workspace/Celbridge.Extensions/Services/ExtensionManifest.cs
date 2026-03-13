using Celbridge.Documents;

namespace Celbridge.Extensions;

/// <summary>
/// The type of document editor provided by an extension.
/// </summary>
public enum DocumentEditorType
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
public record CodePreviewConfig
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
/// Code editor configuration for code extensions.
/// Combines editor options and customization script path.
/// </summary>
public record CodeEditorConfig
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
/// A document file type declared by an extension.
/// Declares the file extension the editor handles and an optional display name or localization key
/// shown in the Add File dialog.
/// </summary>
public record DocumentFileType
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
/// A document template declared by an extension.
/// Templates provide starter content for new files of the extension's type.
/// </summary>
public partial record DocumentTemplate
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
/// Extension identity, permissions, and hosting information.
/// Shared across all contributions from the same extension.
/// </summary>
public partial record ExtensionInfo
{
    /// <summary>
    /// Unique identifier for the extension (e.g., "celbridge.notes").
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Display name of the extension (from extension.toml).
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Optional feature flag. When set, all contributions are disabled if this feature is off.
    /// </summary>
    public string? FeatureFlag { get; init; }

    /// <summary>
    /// The directory containing the extension (set during loading, not from TOML).
    /// </summary>
    public string ExtensionDirectory { get; init; } = string.Empty;

    /// <summary>
    /// A unique virtual host name for this extension's assets (set during loading, not from TOML).
    /// </summary>
    public string HostName { get; init; } = string.Empty;
}

/// <summary>
/// A single document editor contribution parsed from a TOML document manifest.
/// Each extension can contribute one or more document editors via its extension.toml.
/// </summary>
public partial record DocumentContribution
{
    /// <summary>
    /// The parent extension that provides this contribution.
    /// </summary>
    public ExtensionInfo Extension { get; init; } = new();

    /// <summary>
    /// Unique identifier for this document contribution (e.g., "note-document").
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// The type of editor: "custom" (full WebView2) or "code" (Monaco-based).
    /// </summary>
    public DocumentEditorType Type { get; init; }

    /// <summary>
    /// The document file types this editor handles. Each entry declares the file extension and
    /// an optional display name or localization key for the Add File dialog.
    /// </summary>
    public IReadOnlyList<DocumentFileType> FileTypes { get; init; } = [];

    /// <summary>
    /// Entry point for custom editors (e.g., "index.html").
    /// For code editors, this is optional.
    /// </summary>
    public string? EntryPoint { get; init; }

    /// <summary>
    /// Priority for conflict resolution when multiple editors support the same extension.
    /// </summary>
    public EditorPriority Priority { get; init; }

    /// <summary>
    /// Optional list of document templates provided by this extension.
    /// </summary>
    public IReadOnlyList<DocumentTemplate> Templates { get; init; } = [];

    /// <summary>
    /// Preview configuration for code editors. When present, enables the split editor.
    /// </summary>
    public CodePreviewConfig? CodePreview { get; init; }

    /// <summary>
    /// Code editor configuration for code extensions.
    /// </summary>
    public CodeEditorConfig? CodeEditor { get; init; }
}
