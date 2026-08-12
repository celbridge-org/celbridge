#if WINDOWS
using Celbridge.UserInterface.Services;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Broadcasts window activation on the packaged WinUI head, so services can refresh state that may have
/// changed while the application was in the background.
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

        if (activationState == WindowActivationState.PointerActivated
            || activationState == WindowActivationState.CodeActivated)
        {
            var message = new MainWindowActivatedMessage();
            _messengerService.Send(message);
        }
    }
}
#endif
