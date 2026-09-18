namespace Celbridge.Workspace;

/// <summary>
/// Tracks which workspace areas the layout shows, which are on screen, and how far the Bottom area spans.
/// </summary>
public interface ILayoutService
{
    /// <summary>
    /// The areas the layout shows, always including Main. Focus and Presentation show Main alone, standing for
    /// whichever document area the mode puts on screen.
    /// </summary>
    IReadOnlySet<WorkspaceArea> VisibleAreas { get; }

    /// <summary>
    /// The areas currently on screen, which in Focus and Presentation is the one document area the mode is
    /// showing. Safe to read from any thread.
    /// </summary>
    IReadOnlySet<WorkspaceArea> PresentedAreas { get; }

    /// <summary>
    /// How far the Bottom document area spans across the workspace.
    /// </summary>
    BottomAreaAlignment BottomAreaAlignment { get; }

    /// <summary>
    /// Whether the layout shows the area. Always true for Main.
    /// </summary>
    bool IsAreaVisible(WorkspaceArea area);

    /// <summary>
    /// Shows or hides an area. Fails for Main, which is always visible.
    /// </summary>
    Result SetAreaVisibility(WorkspaceArea area, bool isVisible);

    /// <summary>
    /// Hides the area when it is showing and shows it when it is hidden. Fails for Main.
    /// </summary>
    Result ToggleAreaVisibility(WorkspaceArea area);

    /// <summary>
    /// Sets how far the Bottom document area spans across the workspace.
    /// </summary>
    void SetBottomAreaAlignment(BottomAreaAlignment alignment);
}
