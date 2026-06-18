namespace Celbridge.UserInterface;

/// <summary>
/// Severity of a status message shown to the user, e.g. in an InfoBar.
/// UI-agnostic so a view model can set it without referencing a presentation
/// framework; a converter maps it to the control's own severity type.
/// </summary>
public enum StatusSeverity
{
    /// <summary>
    /// Neutral information, or an in-progress state.
    /// </summary>
    Informational,

    /// <summary>
    /// An operation completed successfully.
    /// </summary>
    Success,

    /// <summary>
    /// A non-blocking caution the user should notice.
    /// </summary>
    Warning,

    /// <summary>
    /// An operation failed, or input is invalid.
    /// </summary>
    Error
}
