#if !WINDOWS
using Celbridge.UserInterface.Services;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Broadcasts window activation and deactivation on the Skia desktop heads, so services can refresh state
/// that may have changed while the application was in the background, and can tell the application losing the
/// keyboard from focus moving within it.
/// </summary>
internal sealed class SkiaWindowActivationMonitor : IWindowActivationMonitor
{
    private readonly IMessengerService _messengerService;

    public SkiaWindowActivationMonitor(IMessengerService messengerService)
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

        if (activationState == Windows.UI.Core.CoreWindowActivationState.PointerActivated
            || activationState == Windows.UI.Core.CoreWindowActivationState.CodeActivated)
        {
            var activatedMessage = new MainWindowActivatedMessage();
            _messengerService.Send(activatedMessage);

            return;
        }

        var deactivatedMessage = new MainWindowDeactivatedMessage();
        _messengerService.Send(deactivatedMessage);
    }
}
#endif
