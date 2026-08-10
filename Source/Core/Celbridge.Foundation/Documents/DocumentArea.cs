namespace Celbridge.Documents;

/// <summary>
/// A division of the Documents panel that hosts document tabs. An area shows one section, or two when
/// it is split.
/// </summary>
public enum DocumentArea
{
    /// <summary>
    /// The centre area. Always visible, and splits into left and right sections.
    /// </summary>
    Main,

    /// <summary>
    /// The collapsible area below Main. Splits into left and right sections.
    /// </summary>
    Bottom,

    /// <summary>
    /// The collapsible area to the right, spanning the full height of the workspace body. Splits into
    /// top and bottom sections.
    /// </summary>
    Side
}
