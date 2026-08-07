using Celbridge.UserInterface.Services;

namespace Celbridge.UserInterface.Platform;

internal sealed class OverlayInputSuppressor : IOverlayInputSuppressor
{
    public void SuppressWhileOpen(FlyoutBase flyout)
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
        };
    }
}
