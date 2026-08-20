using Celbridge.Logging;
using Celbridge.UserInterface;

namespace Celbridge.WorkspaceUI.Services;

/// <summary>
/// Tracks the focused workspace panel and coordinates release of focus from the surface losing it.
/// </summary>
public class FocusService : IFocusService
{
    private readonly IMessengerService _messengerService;
    private readonly ILogger<FocusService> _logger;
    private readonly Dictionary<WorkspacePanelId, Action> _panelFocusHandlers = new();
    private WorkspacePanelId _focusedPanel = WorkspacePanelId.None;
    private IEditTarget? _editTarget;

    // The release callback matters on the Skia heads, where WebView and host focus are not integrated: a
    // native panel taking focus would otherwise leave a WebView editor's DOM caret active.
    private Action? _releaseFocusedSurface;

    // Identifies the surface that release callback belongs to. Two surfaces in the same panel that both carry
    // no edit target (two .webview documents) are otherwise indistinguishable from one surface re-reporting.
    private IFocusSurface? _focusedSurface;

    public FocusService(
        IMessengerService messengerService,
        ILogger<FocusService> logger)
    {
        _messengerService = messengerService;
        _logger = logger;

        // Leaving the workspace for another page clears panel focus, so returning shows no focused panel
        // until the user clicks or tabs into one.
        _messengerService.Register<WorkspacePageDeactivatedMessage>(this, OnWorkspacePageDeactivated);
    }

    public WorkspacePanelId FocusedPanel => _focusedPanel;

    public IEditTarget? EditTarget => _editTarget;

    public void OnFocusReceived(FocusClaim claim)
    {
        var previousPanel = _focusedPanel;
        var previousSurface = _focusedSurface;
        var releasePreviousSurface = _releaseFocusedSurface;

        // Surface identity alone separates a move off a surface from that surface re-reporting its own
        // focus. Neither the panel nor the edit target can stand in for it: two .webview documents both claim
        // Documents and carry no edit target, and managed chrome taking the keyboard inside the panel a
        // surface holds it for (the URL bar, the find bar) is a move with nothing else to show for it.
        var surfaceChanged = !ReferenceEquals(claim.Surface, previousSurface);

        _focusedPanel = claim.Panel;
        _focusedSurface = claim.Surface;
        _releaseFocusedSurface = claim.ReleaseFocus;

        // The edit context follows edit intent, not the caret. A claim carrying a target replaces it; a
        // target-less claim (chrome focus, or None) preserves the last editing surface so Edit commands
        // still route there.
        if (claim.EditTarget is not null)
        {
            _editTarget = claim.EditTarget;
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

    public void SetPanelFocusHandler(WorkspacePanelId panel, Action? focusHandler)
    {
        if (focusHandler is null)
        {
            _panelFocusHandlers.Remove(panel);
            return;
        }

        _panelFocusHandlers[panel] = focusHandler;
    }

    public void RefocusFocusedPanel()
    {
        if (_panelFocusHandlers.TryGetValue(_focusedPanel, out var focusHandler))
        {
            focusHandler.Invoke();
        }
    }

    // How a claim reads in a focus log: what took the keyboard, and for a web surface which one.
    private static string DescribeClaim(FocusClaim claim)
    {
        if (claim.Kind == FocusClaimKind.ManagedControl)
        {
            return claim.Panel == WorkspacePanelId.None ? "nothing" : "managed control";
        }

        return $"web surface {claim.Surface?.SurfaceName ?? "unnamed"}";
    }

    private void OnWorkspacePageDeactivated(object recipient, WorkspacePageDeactivatedMessage message)
    {
        // The destination page will take focus; clearing here makes the workspace deterministically show no
        // focused panel on return, rather than depending on whether that page grabs focus.
        ClearFocus();

        // ClearFocus preserves the edit context on purpose, so Edit commands still reach the last editing
        // surface while focus sits on chrome. A workspace going away takes its surfaces with it, so the
        // target is dropped outright here: this service outlives the workspace, and the Edit menu asks it
        // what can be edited.
        _editTarget = null;

        // The interaction state the tracker consults is dropped on the same boundary, so a hold or an open
        // popup left behind by the outgoing workspace cannot suppress focus reporting in the next one.
        FocusIntent.Reset();
    }
}
