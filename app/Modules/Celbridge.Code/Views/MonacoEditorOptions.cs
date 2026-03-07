namespace Celbridge.Code.Views;

/// <summary>
/// Configuration options for the Monaco editor.
/// These options are passed to Monaco during initialization.
/// </summary>
public record MonacoEditorOptions
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
    /// Default options suitable for most editing scenarios.
    /// </summary>
    public static MonacoEditorOptions Default => new();
}
