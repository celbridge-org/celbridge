using Celbridge.Logging;

namespace Celbridge.WorkspaceUI.Services;

/// <summary>
/// Tracks the focused workspace panel and coordinates release of focus from the surface losing it.
/// </summary>
public class FocusService : IFocusService
{
    private readonly IMessengerService _messengerService;
    private readonly ILogger<FocusService> _logger;
    private readonly Dictionary<FocusPanelId, Action> _panelFocusHandlers = new();
    private FocusPanelId _focusedPanel = FocusPanelId.None;
    private FocusPanelId _heldPanel = FocusPanelId.None;
    private IEditTarget? _editTarget;

    // The release callback matters on the Skia heads, where WebView and host focus are not integrated: a
    // native panel taking focus would otherwise leave a WebView editor's DOM caret active.
    private Action? _releaseFocusedSurface;

    // Identifies the surface that release callback belongs to. Without it, two surfaces in the same panel
    // look the same as one surface reporting its focus twice.
    private IFocusSurface? _focusedSurface;

    public FocusService(
        IMessengerService messengerService,
        ILogger<FocusService> logger)
    {
        _messengerService = messengerService;
        _logger = logger;

        // This service outlives the workspace, so the state it holds about the workspace's surfaces is
        // dropped when that workspace goes away.
        _messengerService.Register<WorkspaceUnloadedMessage>(this, OnWorkspaceUnloaded);
    }

    public FocusPanelId FocusedPanel => _focusedPanel;

    public FocusPanelId HeldPanel => _heldPanel;

    public IEditTarget? EditTarget => _editTarget;

    public void HoldPanelUntilNextInput(FocusPanelId panel)
    {
        _heldPanel = panel;
    }

    public void EndPanelHold()
    {
        _heldPanel = FocusPanelId.None;
    }

    public void OnFocusReceived(FocusClaim claim)
    {
        var previousPanel = _focusedPanel;
        var previousSurface = _focusedSurface;
        var releasePreviousSurface = _releaseFocusedSurface;

        // Only the surface identity tells a move off a surface apart from that surface reporting its focus
        // again. The panel cannot: two documents in the same area both claim Documents, and chrome taking
        // the keyboard inside a surface's panel (the URL bar, the find bar) does not change it either.
        var surfaceChanged = !ReferenceEquals(claim.Surface, previousSurface);

        _focusedPanel = claim.Panel;
        _focusedSurface = claim.Surface;
        _releaseFocusedSurface = claim.ReleaseFocus;

        // A claim with a target replaces the current one. A claim without one clears it, so Edit commands
        // cannot act on a surface the user has moved away from, unless the claim came from chrome.
        if (claim.EditTarget is not null)
        {
            _editTarget = claim.EditTarget;
        }
        else if (!PreservesEditTarget(claim, previousPanel, previousSurface))
        {
            _editTarget = null;
        }

        // State is updated before the release so a re-entrant report triggered by it observes the new
        // surface rather than the outgoing one.
        if (surfaceChanged)
        {
            releasePreviousSurface?.Invoke();

            _logger.LogDebug(
                "Released the web surface {PreviousSurface} in {PreviousPanel} to {Surface} in {Panel}",
                previousSurface?.SurfaceName ?? "none",
                previousPanel,
                claim.Surface?.SurfaceName ?? "none",
                claim.Panel);
        }

        if (claim.Panel == previousPanel)
        {
            return;
        }

        _logger.LogTrace(
            "Panel focus {PreviousPanel} -> {Panel}, edit target {EditTarget}, claimed by {Claim}",
            previousPanel,
            claim.Panel,
            _editTarget?.GetType().Name ?? "none",
            DescribeClaim(claim));

        var message = new PanelFocusChangedMessage(claim.Panel);
        _messengerService.Send(message);
    }

    public void ClearFocus()
    {
        var claim = FocusClaim.None();
        OnFocusReceived(claim);
    }

    public void ClearEditTarget(IEditTarget target)
    {
        if (!ReferenceEquals(_editTarget, target))
        {
            return;
        }

        _editTarget = null;

        _logger.LogDebug("Edit target cleared on teardown: {EditTarget}", target.GetType().Name);
    }

    public void SetPanelFocusHandler(FocusPanelId panel, Action? focusHandler)
    {
        if (focusHandler is null)
        {
            _panelFocusHandlers.Remove(panel);
            return;
        }

        _panelFocusHandlers[panel] = focusHandler;
    }

    public void RefocusPanel(FocusPanelId panel)
    {
        if (_panelFocusHandlers.TryGetValue(panel, out var focusHandler))
        {
            focusHandler.Invoke();
        }
    }

    // Whether a claim with no edit target leaves the current one alone. Two kinds of chrome take the
    // keyboard without ending the edit: chrome outside the panels (the toolbar, the menu), which claims no
    // panel, and a managed control inside the panel a web surface holds (a URL bar, a find bar). Any other
    // claim means the user has moved somewhere with nothing editable, so the target is stale.
    private static bool PreservesEditTarget(
        FocusClaim claim,
        FocusPanelId previousPanel,
        IFocusSurface? previousSurface)
    {
        if (claim.Panel == FocusPanelId.None)
        {
            return true;
        }

        return claim.Kind == FocusClaimKind.ManagedControl
            && claim.Panel == previousPanel
            && previousSurface is not null;
    }

    // How a claim reads in a focus log: what took the keyboard, and for a web surface which one.
    private static string DescribeClaim(FocusClaim claim)
    {
        if (claim.Kind == FocusClaimKind.ManagedControl)
        {
            return claim.Panel == FocusPanelId.None ? "nothing" : "managed control";
        }

        return $"web surface {claim.Surface?.SurfaceName ?? "unnamed"}";
    }

    private void OnWorkspaceUnloaded(object recipient, WorkspaceUnloadedMessage message)
    {
        // The surfaces went with the workspace, so the release callback is dropped rather than invoked:
        // there is no caret left to drop, and calling it would reach into a torn-down web view.
        _releaseFocusedSurface = null;
        _focusedSurface = null;

        // ClearFocus preserves the edit context on purpose, so Edit commands still reach the last editing
        // surface while focus sits on chrome. A workspace going away takes its surfaces with it, so the
        // target is dropped outright here: the Edit menu asks this service what can be edited.
        _editTarget = null;

        ClearFocus();

        // A hold waits on the next user input, which a workspace teardown can pre-empt, so it is dropped on
        // the same boundary rather than carrying into the next workspace.
        EndPanelHold();
    }
}
