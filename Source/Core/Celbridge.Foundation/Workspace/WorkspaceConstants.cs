namespace Celbridge.Workspace;

public static class WorkspaceConstants
{
    /// <summary>
    /// The smallest width at which a document is legible. Every minimum in the workspace layout is composed
    /// upward from this value and DocumentMinHeight, and both are set as generously as the minimum window size
    /// allows: three of these sit side by side in the default layout, so the width is close to its ceiling.
    /// Mirrors the --cel-document-min-width design token.
    /// </summary>
    public const double DocumentMinWidth = 230;

    /// <summary>
    /// The smallest height at which a document is legible. This is also the shortest a document area can be
    /// dragged, so it is held below its ceiling to leave the Bottom area usable as a strip. Mirrors the
    /// --cel-document-min-height design token.
    /// </summary>
    public const double DocumentMinHeight = 200;

    /// <summary>
    /// The edge a document section draws down each of its sides, part of the chrome it takes around the
    /// document it hosts.
    /// </summary>
    public const double SectionEdgeThickness = 1;

    /// <summary>
    /// The height of the tab strip band above a document section's content. The live layout measures the band
    /// the section template builds and takes the taller of the two, so this is the floor under the measurement
    /// as well as the value used where there is no band to measure: before the template is applied, and for the
    /// window minimum, which is composed before any workspace exists.
    /// </summary>
    public const double SectionTabStripHeight = 40;

    /// <summary>
    /// Width of the icon rail down the side of the Utility Panel. Mirrored by the --cel-rail-width design
    /// token.
    /// </summary>
    public const double UtilityPanelRailWidth = 50;

    /// <summary>
    /// Default width of the Utility Panel.
    /// </summary>
    public const float UtilityPanelWidth = 300f;

    /// <summary>
    /// Default width of the Side document area.
    /// </summary>
    public const float SideAreaWidth = 300f;

    /// <summary>
    /// Default height of the Bottom document area.
    /// </summary>
    public const float BottomAreaHeight = 350f;

    /// <summary>
    /// Default alignment of the Bottom document area.
    /// </summary>
    public const BottomAreaAlignment BottomAreaAlignment = Celbridge.Workspace.BottomAreaAlignment.Center;
}
