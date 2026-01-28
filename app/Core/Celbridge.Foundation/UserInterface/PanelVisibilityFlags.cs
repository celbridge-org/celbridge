namespace Celbridge.UserInterface;

/// <summary>
/// Flags representing which workspace panels should be visible.
/// </summary>
[Flags]
public enum PanelVisibilityFlags
{
    /// <summary>
    /// No panels visible.
    /// </summary>
    None = 0,

    /// <summary>
    /// Context panel (left sidebar with explorer/search) is visible.
    /// </summary>
    Primary = 1 << 0,

    /// <summary>
    /// Inspector panel (right sidebar) is visible.
    /// </summary>
    Secondary = 1 << 1,

    /// <summary>
    /// Console panel (bottom panel) is visible.
    /// </summary>
    Console = 1 << 2,

    /// <summary>
    /// All panels are visible.
    /// </summary>
    All = Primary | Secondary | Console
}
