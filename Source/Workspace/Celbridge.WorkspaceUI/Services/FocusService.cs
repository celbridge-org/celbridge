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
        var panel = claim.Panel;
        var target = claim.EditTarget;

        if (panel != _focusedPanel)
        {
            var previousPanel = _focusedPanel;
            var releasePreviousFocus = _releaseFocusedSurface;

            _focusedPanel = panel;
            _releaseFocusedSurface = claim.ReleaseFocus;

            // The edit context follows edit intent, not the caret. A claim carrying a target replaces it; a
            // target-less claim (chrome focus, or None) preserves the last editing surface so Edit commands
            // still route there. The caret is always released below regardless.
            if (target is not null)
            {
                _editTarget = target;
            }

            // Release the surface we just left. State is updated first so that a re-entrant focus
            // report triggered by the release observes the new panel rather than the old one.
            releasePreviousFocus?.Invoke();

            _logger.LogTrace(
                "Panel focus {PreviousPanel} -> {Panel}, edit target {EditTarget}",
                previousPanel,
                panel,
                _editTarget?.GetType().Name ?? "none");

            var message = new PanelFocusChangedMessage(panel);
            _messengerService.Send(message);

            return;
        }

        // Focus stayed on the same panel, but a different surface within it is reporting (e.g. switching
        // between two document-section editors). Release the previous surface first so its DOM caret does
        // not stay active on the Skia heads, updating state before the release so a re-entrant report
        // observes the new surface.
        if (target is not null
            && !ReferenceEquals(target, _editTarget))
        {
            var releasePreviousFocus = _releaseFocusedSurface;

            _editTarget = target;
            _releaseFocusedSurface = claim.ReleaseFocus;

            releasePreviousFocus?.Invoke();

            _logger.LogDebug(
                "Edit target changed within {Panel} to {EditTarget}",
                panel,
                _editTarget.GetType().Name);

            return;
        }

        // The same surface is re-reporting (e.g. a bubbled event). Adopt a target or release callback when
        // provided, so a report carrying neither cannot clear them.
        if (target is not null)
        {
            _editTarget = target;
        }

        // The surface re-reporting its own focus is not a move off it, so it adopts the newer callback and
        // keeps its caret.
        if (claim.Kind == FocusClaimKind.WebSurface)
        {
            _releaseFocusedSurface = claim.ReleaseFocus;
            return;
        }

        // Managed chrome has taken the keyboard inside the panel a web surface holds it for: the URL bar and
        // the find bar both sit in the Documents panel. That is a move off the surface even though the panel
        // is unchanged, so release it. Without this the surface stays the focused one and focus
        // reconciliation hands the keyboard straight back, leaving the chrome unfocusable.
        var releaseFocusedSurface = _releaseFocusedSurface;
        if (releaseFocusedSurface is null)
        {
            return;
        }

        _releaseFocusedSurface = null;
        releaseFocusedSurface.Invoke();

        _logger.LogDebug("Released the focused surface in {Panel} to managed chrome", panel);
    }

    public void ClearFocus()
    {
        // Leaving the workspace hands the keyboard to managed content on another page, which belongs to no
        // workspace panel.
        var claim = FocusClaim.FromManagedControl(WorkspacePanelId.None);
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

    private void OnWorkspacePageDeactivated(object recipient, WorkspacePageDeactivatedMessage message)
    {
        // The destination page will take focus; clearing here makes the workspace deterministically show no
        // focused panel on return, rather than depending on whether that page grabs focus.
        ClearFocus();

        // The interaction state the tracker consults is dropped on the same boundary, so a hold or an open
        // popup left behind by the outgoing workspace cannot suppress focus reporting in the next one.
        FocusIntent.Reset();
    }
}
