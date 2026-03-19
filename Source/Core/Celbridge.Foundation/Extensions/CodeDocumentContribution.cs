namespace Celbridge.Extensions;

/// <summary>
/// Preview configuration for a code extension.
/// When present, enables the split editor with a preview panel.
/// </summary>
public record CodePreviewConfig
{
    /// <summary>
    /// The preview page to load in the split editor preview panel,
    /// relative to the extension directory (e.g., "markdown-preview/index.html").
    /// </summary>
    public string EntryPoint { get; init; } = string.Empty;
}

/// <summary>
/// Monaco editor configuration for code extensions.
/// </summary>
public record CodeEditorConfig
{
    /// <summary>
    /// Whether the editor allows scrolling past the last line of content.
    /// </summary>
    public bool? ScrollBeyondLastLine { get; init; }

    /// <summary>
    /// Whether long lines wrap to the next visual line instead of scrolling horizontally.
    /// </summary>
    public bool? WordWrap { get; init; }

    /// <summary>
    /// Whether the minimap overview is displayed in the editor gutter.
    /// </summary>
    public bool? MinimapEnabled { get; init; }

    /// <summary>
    /// Path to a JavaScript customization script, relative to the extension directory (e.g., "customize.js").
    /// The script is loaded after Monaco initializes and should export an
    /// activate(monaco, editor, container, celbridge) function.
    /// </summary>
    public string? CustomizationScript { get; init; }
}

/// <summary>
/// A Monaco-based code editor contribution.
/// </summary>
public partial record CodeDocumentContribution : DocumentContribution
{
    /// <summary>
    /// Code editor configuration.
    /// </summary>
    public CodeEditorConfig? CodeEditor { get; init; }

    /// <summary>
    /// Preview configuration. When present, enables the split editor with a preview panel.
    /// </summary>
    public CodePreviewConfig? CodePreview { get; init; }
}
