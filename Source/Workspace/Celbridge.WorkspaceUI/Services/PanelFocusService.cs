namespace Celbridge.WorkspaceUI.Services;

/// <summary>
/// Manages focus state across panels to ensure only one panel appears focused at a time.
/// </summary>
public class PanelFocusService : IPanelFocusService
{
    private readonly IMessengerService _messengerService;
    private WorkspacePanel _focusedPanel = WorkspacePanel.None;

    public PanelFocusService(IMessengerService messengerService)
    {
        _messengerService = messengerService;
    }

    public WorkspacePanel FocusedPanel => _focusedPanel;

    public void SetFocusedPanel(WorkspacePanel panel)
    {
        if (_focusedPanel == panel)
        {
            return;
        }

        _focusedPanel = panel;

        var message = new PanelFocusChangedMessage(panel);
        _messengerService.Send(message);
    }

    public void ClearFocus()
    {
        SetFocusedPanel(WorkspacePanel.None);
    }
}
