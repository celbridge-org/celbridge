using Celbridge.Logging;

namespace Celbridge.WorkspaceUI.Services;

/// <summary>
/// Tracks the focused workspace panel and coordinates release of focus from the surface losing it.
/// </summary>
public class FocusService : IFocusService
{
    private readonly IMessengerService _messengerService;
    private readonly ILogger<FocusService> _logger;
    private readonly Dictionary<WorkspacePanel, Action> _panelFocusHandlers = new();
    private WorkspacePanel _focusedPanel = WorkspacePanel.None;
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
    }

    public WorkspacePanel FocusedPanel => _focusedPanel;

    public IEditTarget? EditTarget => _editTarget;

    public void OnFocusReceived(WorkspacePanel panel, IEditTarget? target = null, Action? onReleaseFocus = null)
    {
        if (panel != _focusedPanel)
        {
            var previousPanel = _focusedPanel;
            var releasePreviousFocus = _releaseFocusedSurface;

            _focusedPanel = panel;
            _releaseFocusedSurface = onReleaseFocus;

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

            _logger.LogDebug(
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
            _releaseFocusedSurface = onReleaseFocus;

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

        if (onReleaseFocus is not null)
        {
            _releaseFocusedSurface = onReleaseFocus;
        }
    }

    public void ClearFocus()
    {
        OnFocusReceived(WorkspacePanel.None);
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

    public void SetPanelFocusHandler(WorkspacePanel panel, Action? focusHandler)
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
}
