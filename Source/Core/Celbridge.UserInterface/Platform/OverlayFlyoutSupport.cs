using Celbridge.UserInterface.Services;
using Celbridge.WebHost;

namespace Celbridge.UserInterface.Platform;

internal sealed class OverlayFlyoutSupport : IOverlayFlyoutSupport
{
    private readonly IFocusReconciler _focusReconciler;

    public OverlayFlyoutSupport(IFocusReconciler focusReconciler)
    {
        _focusReconciler = focusReconciler;
    }

    public void Apply(FlyoutBase flyout)
    {
        // The scope lives in the handlers the flyout itself holds, so nothing outside the flyout keeps it
        // alive and a per-instance flyout (a document tab's context menu) is collected with its owner.
        IDisposable? suppressionScope = null;

        flyout.Opened += (_, _) =>
        {
            // Defensive: a second Opened without an intervening Closed would otherwise strand a scope, and
            // a stranded scope leaves every hosted web view ignoring the mouse for the rest of the session.
            suppressionScope?.Dispose();
            suppressionScope = MacOSWebViewInputSuppressor.Suppress();
        };

        flyout.Closed += (_, _) =>
        {
            suppressionScope?.Dispose();
            suppressionScope = null;

            // Uno leaves managed focus on the item that was focused inside the flyout, having already taken
            // that item out of the visual tree, so the keys the user types next reach a menu they have
            // dismissed. Reconciling gives the keyboard back to whatever should hold it.
            _focusReconciler.Reconcile();
        };
    }
}
