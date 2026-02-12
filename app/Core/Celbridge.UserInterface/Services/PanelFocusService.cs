using Celbridge.Messaging;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Manages focus state across panels to ensure only one panel appears focused at a time.
/// </summary>
public class PanelFocusService : IPanelFocusService
{
    private readonly IMessengerService _messengerService;
    private FocusablePanel _focusedPanel = FocusablePanel.None;

    public PanelFocusService(IMessengerService messengerService)
    {
        _messengerService = messengerService;
    }

    public FocusablePanel FocusedPanel => _focusedPanel;

    public void SetFocusedPanel(FocusablePanel panel)
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
        SetFocusedPanel(FocusablePanel.None);
    }
}
