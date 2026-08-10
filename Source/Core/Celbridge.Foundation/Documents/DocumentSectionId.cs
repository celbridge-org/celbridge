namespace Celbridge.Documents;

/// <summary>
/// Identifies one of the six document tab strips. Values are persisted and sent to agents by name, not
/// by number.
/// </summary>
public enum DocumentSectionId
{
    /// <summary>
    /// The Main area's primary section. The one section that always exists and is always visible.
    /// </summary>
    MainLeft,

    /// <summary>
    /// The Main area's secondary section, present only while Main is split.
    /// </summary>
    MainRight,

    /// <summary>
    /// The Bottom area's primary section.
    /// </summary>
    BottomLeft,

    /// <summary>
    /// The Bottom area's secondary section, present only while Bottom is split.
    /// </summary>
    BottomRight,

    /// <summary>
    /// The Side area's primary section.
    /// </summary>
    SideTop,

    /// <summary>
    /// The Side area's secondary section, present only while Side is split.
    /// </summary>
    SideBottom
}
