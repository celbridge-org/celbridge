namespace Celbridge.Workspace;

/// <summary>
/// A place in the workspace that presents a workspace item, whether a workspace-scoped utility or an open
/// document. A workspace item occupies exactly one area at a time; moving a utility between areas reparents
/// its single live view rather than recreating it.
/// </summary>
public enum WorkspaceArea
{
    /// <summary>
    /// A place in the Utility Panel, the collapsible sidebar on the left, which shows one workspace item
    /// at a time.
    /// </summary>
    Utility,

    /// <summary>
    /// A tab in the centre document area, which is always visible.
    /// </summary>
    Main,

    /// <summary>
    /// A tab in the collapsible document area below Main.
    /// </summary>
    Bottom,

    /// <summary>
    /// A tab in the collapsible document area to the right.
    /// </summary>
    Side
}

/// <summary>
/// How far the Bottom document area spans across the workspace. The area always sits below the Main area;
/// the alignment decides whether it also runs under the Utility Panel, the Side area, or both, and the
/// panels it runs under stop above it.
/// </summary>
public enum BottomAreaAlignment
{
    /// <summary>
    /// The Bottom area spans the Main area only, leaving the Utility Panel and Side area full height.
    /// </summary>
    Center,

    /// <summary>
    /// The Bottom area spans the Utility Panel and the Main area, leaving the Side area full height.
    /// </summary>
    Left,

    /// <summary>
    /// The Bottom area spans the Main area and the Side area, leaving the Utility Panel full height.
    /// </summary>
    Right,

    /// <summary>
    /// The Bottom area spans the whole workspace, so neither the Utility Panel nor the Side area runs
    /// past it.
    /// </summary>
    Justify
}
