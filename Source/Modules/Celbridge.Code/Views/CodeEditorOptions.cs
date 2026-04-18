namespace Celbridge.Code.Views;

/// <summary>
/// Configuration options for the code editor.
/// </summary>
public record CodeEditorOptions
{
    /// <summary>
    /// Whether to allow scrolling beyond the last line of content.
    /// When false, scrolling stops at the last line.
    /// Default is true (Monaco's default behavior).
    /// </summary>
    public bool ScrollBeyondLastLine { get; init; } = true;

    /// <summary>
    /// Whether word wrap is enabled.
    /// Default is true.
    /// </summary>
    public bool WordWrap { get; init; } = true;

    /// <summary>
    /// Whether the minimap is visible.
    /// Default is true with autohide enabled.
    /// </summary>
    public bool MinimapEnabled { get; init; } = true;

    /// <summary>
    /// URL of an ES module that implements the split-editor preview contract
    /// When non-null, the editor loads the module into a sandboxed iframe alongside Monaco.
    /// When null, the editor is a plain code view.
    /// </summary>
    public string? PreviewRendererUrl { get; init; } = null;

    /// <summary>
    /// Default options suitable for most editing scenarios.
    /// </summary>
    public static CodeEditorOptions Default => new();
}
