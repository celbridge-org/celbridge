namespace Celbridge.Workspace;

public static class WorkspaceConstants
{
    /// <summary>
    /// The smallest width at which a document is legible. Every minimum in the workspace layout is composed
    /// upward from this value and DocumentMinHeight. Mirrors the --cel-document-min-width design token.
    /// </summary>
    public const double DocumentMinWidth = 200;

    /// <summary>
    /// The smallest height at which a document is legible. Mirrors the --cel-document-min-height design
    /// token.
    /// </summary>
    public const double DocumentMinHeight = 120;

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
}
