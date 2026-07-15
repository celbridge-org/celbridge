namespace Celbridge.Workspace;

/// <summary>
/// Where a utility is currently docked. A utility always occupies exactly one dock location; moving it between
/// locations reparents its single live WebView rather than recreating it. UtilityPanel is a rail surface in the
/// Utility Panel; Document is a tab in the documents area. A future free-floating window would be a third
/// location, which is why this is a location rather than a docked/undocked flag.
/// </summary>
public enum DockLocation
{
    /// <summary>
    /// A rail surface in the Utility Panel, shown one at a time alongside Explorer and Search.
    /// </summary>
    UtilityPanel,

    /// <summary>
    /// A tab in the documents area, sitting among the open documents.
    /// </summary>
    Document,
}
