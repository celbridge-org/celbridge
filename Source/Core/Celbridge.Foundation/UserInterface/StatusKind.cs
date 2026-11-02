namespace Celbridge.UserInterface;

/// <summary>
/// The kind of state a status marks. The status pip shows each kind in its own color.
/// </summary>
public enum StatusKind
{
    /// <summary>
    /// Neutral information, or an in-progress state.
    /// </summary>
    Info,

    /// <summary>
    /// A state that went well, like a finished task.
    /// </summary>
    Success,

    /// <summary>
    /// A non-blocking caution the user should notice.
    /// </summary>
    Warning,

    /// <summary>
    /// An urgent state the user must keep in mind.
    /// </summary>
    Critical
}
