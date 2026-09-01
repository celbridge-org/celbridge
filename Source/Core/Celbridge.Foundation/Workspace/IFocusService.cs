namespace Celbridge.Workspace;

/// <summary>
/// Identifies the parts of the workspace that can hold focus. The grain is what the user perceives as a
/// focused panel rather than a container in the layout.
/// </summary>
public enum FocusPanelId
{
    /// <summary>
    /// Nothing in the workspace holds focus.
    /// </summary>
    None,

    /// <summary>
    /// The Explorer, shown in the Utility Panel rail.
    /// </summary>
    Explorer,

    /// <summary>
    /// Search, shown in the Utility Panel rail.
    /// </summary>
    Search,

    /// <summary>
    /// The document areas, which focus does not distinguish between. A utility docked as a document reports
    /// this rather than CustomUtility.
    /// </summary>
    Documents,

    /// <summary>
    /// Whichever contributed utility the Utility Panel rail is showing.
    /// </summary>
    CustomUtility
}

/// <summary>
/// The identity of a surface that can hold the keyboard. Compared by reference only, so the focus service can
/// tell one surface from another without knowing what a surface is.
/// </summary>
public interface IFocusSurface
{
    /// <summary>
    /// Names the surface in focus diagnostics, so a log line says which surface a claim or a loss came from.
    /// </summary>
    string SurfaceName { get; }
}

/// <summary>
/// What took the keyboard.
/// </summary>
public enum FocusClaimKind
{
    /// <summary>
    /// A managed control in the visual tree, classified by its nearest panel declaration.
    /// </summary>
    ManagedControl,

    /// <summary>
    /// A web surface, reporting through the web-view focus registry. The surface rather than the web view
    /// control: on macOS that control never takes focus at all, the native web view inside it becomes the
    /// window's first responder instead.
    /// </summary>
    WebSurface
}

/// <summary>
/// A report that something has taken the keyboard: what took it, the panel it belongs to, the edit target it
/// offers, and for a web surface its identity and the callback that drops its caret once focus moves on.
/// </summary>
public sealed record FocusClaim
{
    private FocusClaim(
        FocusClaimKind kind,
        FocusPanelId panel,
        IEditTarget? editTarget,
        IFocusSurface? surface,
        Action? releaseFocus)
    {
        Kind = kind;
        Panel = panel;
        EditTarget = editTarget;
        Surface = surface;
        ReleaseFocus = releaseFocus;
    }

    /// <summary>
    /// What took the keyboard.
    /// </summary>
    public FocusClaimKind Kind { get; }

    /// <summary>
    /// The panel the claiming element belongs to, or None when it belongs to no panel.
    /// </summary>
    public FocusPanelId Panel { get; }

    /// <summary>
    /// The surface Edit commands should route to, or null when the claim offers none.
    /// </summary>
    public IEditTarget? EditTarget { get; }

    /// <summary>
    /// Which web surface is claiming, so a claim from a second surface in the same panel can be told from the
    /// holding surface re-reporting its own focus. Null for a managed control.
    /// </summary>
    public IFocusSurface? Surface { get; }

    /// <summary>
    /// Drops the claiming surface's caret once focus moves off it. Null for a managed control, which has no
    /// caret of its own to drop.
    /// </summary>
    public Action? ReleaseFocus { get; }

    /// <summary>
    /// A claim by a managed control in the visual tree.
    /// </summary>
    public static FocusClaim FromManagedControl(FocusPanelId panel, IEditTarget? editTarget = null)
    {
        return new FocusClaim(FocusClaimKind.ManagedControl, panel, editTarget, surface: null, releaseFocus: null);
    }

    /// <summary>
    /// A claim by a web surface. The identity and release callback are required rather than optional:
    /// supplying them is what lets the surface be recognised on a later claim and released when focus moves
    /// off it.
    /// </summary>
    public static FocusClaim FromWebSurface(
        FocusPanelId panel,
        IEditTarget? editTarget,
        IFocusSurface surface,
        Action releaseFocus)
    {
        return new FocusClaim(FocusClaimKind.WebSurface, panel, editTarget, surface, releaseFocus);
    }

    /// <summary>
    /// A claim by nothing at all, for focus leaving the workspace panels entirely.
    /// </summary>
    public static FocusClaim None()
    {
        return new FocusClaim(
            FocusClaimKind.ManagedControl,
            FocusPanelId.None,
            editTarget: null,
            surface: null,
            releaseFocus: null);
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
    FocusPanelId FocusedPanel { get; }

    /// <summary>
    /// The panel that has just been given the keyboard and is being held against stray focus events, or None
    /// when nothing is held. A click keeps producing focus events for a few milliseconds after the work it
    /// triggered has finished, and the focus tracker uses this to tell the document arriving from the
    /// leftovers.
    /// </summary>
    FocusPanelId HeldPanel { get; }

    /// <summary>
    /// The surface that Edit commands route to, or null before any surface has claimed one. Preserved when
    /// focus moves onto chrome or clears, so Edit commands still target the last editing surface; replaced
    /// when a new surface claims focus with a target; cleared when its surface is torn down.
    /// </summary>
    IEditTarget? EditTarget { get; }

    /// <summary>
    /// Holds the given panel until the next thing the user does. Double-clicking a file in the Explorer can
    /// put the keyboard back on the tree just after the document has opened and taken it, leaving the user
    /// typing into the tree while the document looks focused. Nothing tells us a click has finished producing
    /// focus events, so the hold lasts until the user's next click or key press.
    ///
    /// The panel is passed in rather than read from FocusedPanel, because the document's own focus can arrive
    /// after this call: a document built from ordinary controls reports its focus a step later, so the
    /// focused panel does not name it yet.
    /// </summary>
    void HoldPanelUntilNextInput(FocusPanelId panel);

    /// <summary>
    /// Ends the hold, so the next focus change reports its panel again. Called from the window's input
    /// handlers: any real user input means what follows is no longer the previous click's leftovers.
    /// </summary>
    void EndPanelHold();

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
    void SetPanelFocusHandler(FocusPanelId panel, Action? focusHandler);

    /// <summary>
    /// Gives the keyboard back to the given panel by invoking its registered focus handler, so the panel the
    /// focus indicator shows becomes the keyboard target again. The panel is named by the caller rather than
    /// taken from FocusedPanel, because a caller can know which panel should hold the keyboard before the
    /// focus service does: a panel's own focus report may not have arrived yet. A no-op when the panel has no
    /// registered handler.
    /// </summary>
    void RefocusPanel(FocusPanelId panel);
}
