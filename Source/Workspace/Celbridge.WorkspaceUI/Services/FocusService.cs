namespace Celbridge.WorkspaceUI.Services;

/// <summary>
/// Tracks the focused workspace panel and coordinates release of focus from the surface losing it.
/// </summary>
public class FocusService : IFocusService
{
    private readonly IMessengerService _messengerService;
    private WorkspacePanel _focusedPanel = WorkspacePanel.None;
    private IEditTarget? _editTarget;

    // The release callback matters on the Skia heads, where WebView and host focus are not integrated: a
    // native panel taking focus would otherwise leave a WebView editor's DOM caret active.
    private Action? _releaseFocusedSurface;

    public FocusService(IMessengerService messengerService)
    {
        _messengerService = messengerService;
    }

    public WorkspacePanel FocusedPanel => _focusedPanel;

    public IEditTarget? EditTarget => _editTarget;

    public void OnFocusReceived(WorkspacePanel panel, IEditTarget? target = null, Action? onReleaseFocus = null)
    {
        if (panel != _focusedPanel)
        {
            var releasePreviousFocus = _releaseFocusedSurface;

            _focusedPanel = panel;
            _editTarget = target;
            _releaseFocusedSurface = onReleaseFocus;

            // Release the surface we just left. State is updated first so that a re-entrant focus
            // report triggered by the release observes the new panel rather than the old one.
            releasePreviousFocus?.Invoke();

            var message = new PanelFocusChangedMessage(panel);
            _messengerService.Send(message);

            return;
        }

        // Focus stayed on the same panel, but a specific surface within it is reporting. Adopt its target
        // and release callback when provided, so a bubbled report carrying neither cannot clear them.
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
}
