#if WINDOWS
using Celbridge.UserInterface.Services;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Broadcasts window activation changes on the packaged WinUI head, driving the custom title bar's
/// active/inactive tint.
/// </summary>
internal sealed class WindowActivationMonitor : IWindowActivationMonitor
{
    private readonly IMessengerService _messengerService;

    public WindowActivationMonitor(IMessengerService messengerService)
    {
        _messengerService = messengerService;
    }

    public void Start(Window window)
    {
        // No matching unsubscribe: this is an app-lifetime singleton bound to the main window, which lives
        // as long as the process, so the handler never needs detaching.
        window.Activated += OnWindowActivated;
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs e)
    {
        var activationState = e.WindowActivationState;

        if (activationState == WindowActivationState.Deactivated)
        {
            var message = new MainWindowDeactivatedMessage();
            _messengerService.Send(message);
        }
        else if (activationState == WindowActivationState.PointerActivated
            || activationState == WindowActivationState.CodeActivated)
        {
            var message = new MainWindowActivatedMessage();
            _messengerService.Send(message);
        }
    }
}
#endif
