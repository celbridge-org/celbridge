namespace Celbridge.Documents;

/// <summary>
/// Editor priority for conflict resolution when multiple editors support the same file type.
/// Default editors open automatically; Option editors are available via "Open With".
/// </summary>
public enum EditorPriority
{
    /// <summary>
    /// Standard editor for this file type. Opens automatically.
    /// </summary>
    Default,

    /// <summary>
    /// Alternative editor available via "Open With".
    /// </summary>
    Option
}
