using Celbridge.WebHost;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Derives the desired focus state from the focus model and applies it through the platform seams.
/// </summary>
public class FocusReconciler : IFocusReconciler
{
    private readonly IWebViewFocusRegistry _webViewFocusRegistry;
    private readonly IManagedFocusSink _managedFocusSink;
    private readonly IHostWindowFocus _hostWindowFocus;

    public FocusReconciler(
        IWebViewFocusRegistry webViewFocusRegistry,
        IManagedFocusSink managedFocusSink,
        IHostWindowFocus hostWindowFocus)
    {
        _webViewFocusRegistry = webViewFocusRegistry;
        _managedFocusSink = managedFocusSink;
        _hostWindowFocus = hostWindowFocus;
    }

    public void Reconcile()
    {
        var desiredFocus = FocusDerivation.Derive(_webViewFocusRegistry.HasFocusedSurface);

        if (desiredFocus.ParkManagedFocus)
        {
            // Managed focus first: applying managed focus resigns the native first responder, so parking
            // after the native step would undo the focus it establishes.
            _managedFocusSink.TakeFocus();
        }

        if (desiredFocus.FocusWebSurface)
        {
            _webViewFocusRegistry.FocusFocusedSurface();
            return;
        }

        _hostWindowFocus.FocusHostWindow();
    }
}
