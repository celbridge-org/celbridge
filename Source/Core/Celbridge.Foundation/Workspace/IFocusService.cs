namespace Celbridge.Workspace;

/// <summary>
/// What took the keyboard. A claim naming the panel a web surface already holds means opposite things
/// depending on this: a managed control taking it moves the keyboard off that surface, while the surface
/// re-reporting its own focus does not.
/// </summary>
public enum FocusClaimKind
{
    /// <summary>
    /// A managed control in the visual tree, classified by its nearest panel declaration.
    /// </summary>
    ManagedControl,

    /// <summary>
    /// A web surface, reporting through the web-view focus registry.
    /// </summary>
    WebSurface
}

/// <summary>
/// A report that something has taken the keyboard: what took it, the panel it belongs to, the edit target it
/// offers, and for a web surface the callback that drops its caret once focus moves on.
/// </summary>
public sealed record FocusClaim
{
    private FocusClaim(
        FocusClaimKind kind,
        WorkspacePanelId panel,
        IEditTarget? editTarget,
        Action? releaseFocus)
    {
        Kind = kind;
        Panel = panel;
        EditTarget = editTarget;
        ReleaseFocus = releaseFocus;
    }

    /// <summary>
    /// What took the keyboard.
    /// </summary>
    public FocusClaimKind Kind { get; }

    /// <summary>
    /// The panel the claiming element belongs to, or None when it belongs to no panel.
    /// </summary>
    public WorkspacePanelId Panel { get; }

    /// <summary>
    /// The surface Edit commands should route to, or null when the claim offers none.
    /// </summary>
    public IEditTarget? EditTarget { get; }

    /// <summary>
    /// Drops the claiming surface's caret once focus moves off it. Null for a managed control, which has no
    /// caret of its own to drop.
    /// </summary>
    public Action? ReleaseFocus { get; }

    /// <summary>
    /// A claim by a managed control in the visual tree.
    /// </summary>
    public static FocusClaim FromManagedControl(WorkspacePanelId panel, IEditTarget? editTarget = null)
    {
        return new FocusClaim(FocusClaimKind.ManagedControl, panel, editTarget, releaseFocus: null);
    }

    /// <summary>
    /// A claim by a web surface. The release callback is required rather than optional: supplying it
    /// is what allows the surface to be released when focus later moves off it.
    /// </summary>
    public static FocusClaim FromWebSurface(
        WorkspacePanelId panel,
        IEditTarget? editTarget,
        Action releaseFocus)
    {
        return new FocusClaim(FocusClaimKind.WebSurface, panel, editTarget, releaseFocus);
    }
}

/// <summary>
/// Tracks which workspace panel holds focus so that only one panel appears focused at a time, and
/// coordinates release of focus from the surface that is losing it. Panel focus and edit context are
/// distinct: panel focus follows the caret, while the edit context follows the surface that Edit commands
/// should act on and survives focus moving onto chrome.
/// </summary>
public interface IFocusService
{
    /// <summary>
    /// The panel that currently holds focus, or None when focus has left the workspace panels (for example
    /// onto a toolbar or another chrome element).
    /// </summary>
    WorkspacePanelId FocusedPanel { get; }

    /// <summary>
    /// The surface that Edit commands route to, or null before any surface has claimed one. Preserved when
    /// focus moves onto chrome or clears, so Edit commands still target the last editing surface; replaced
    /// when a new surface claims focus with a target; cleared when its surface is torn down.
    /// </summary>
    IEditTarget? EditTarget { get; }

    /// <summary>
    /// Handles a claim of the keyboard: records the claimed panel as the focused one and invokes the previous
    /// surface's release callback. A claim carrying an edit target replaces the current one; a claim without
    /// leaves it in place. A managed control claiming the panel a web surface already holds is chrome
    /// (a URL bar, a find bar) taking the keyboard off that surface, so the surface is released.
    /// </summary>
    void OnFocusReceived(FocusClaim claim);

    /// <summary>
    /// Clears the focused panel to None and releases the surface that holds the caret. The edit context is
    /// preserved, so Edit commands still route to the last editing surface.
    /// </summary>
    void ClearFocus();

    /// <summary>
    /// Clears the edit target if it still references the given surface, so a surface being torn down stops
    /// receiving Edit commands. A newer target is left in place.
    /// </summary>
    void ClearEditTarget(IEditTarget target);

    /// <summary>
    /// Registers how the given panel takes keyboard focus, or null to clear it. Used to return keyboard
    /// focus to the focused panel after an interaction moves it away transiently (a modal dialog closing,
    /// a resource-tree rebuild).
    /// </summary>
    void SetPanelFocusHandler(WorkspacePanelId panel, Action? focusHandler);

    /// <summary>
    /// Re-asserts keyboard focus on the currently focused panel by invoking its registered focus handler,
    /// so the panel the focus indicator shows becomes the keyboard target again. A no-op when the focused
    /// panel has no registered handler.
    /// </summary>
    void RefocusFocusedPanel();
}
