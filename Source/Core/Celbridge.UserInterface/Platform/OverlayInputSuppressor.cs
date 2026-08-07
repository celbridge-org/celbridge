using Celbridge.Platform;

namespace Celbridge.UserInterface.Platform;

internal sealed class OverlayInputSuppressor : IOverlayInputSuppressor
{
    public IDisposable Suppress()
    {
        return MacOSWebViewInputSuppressor.Suppress();
    }
}
