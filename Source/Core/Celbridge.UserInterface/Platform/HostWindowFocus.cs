using Celbridge.UserInterface.Services;

namespace Celbridge.UserInterface.Platform;

internal sealed class HostWindowFocus : IHostWindowFocus
{
    public void FocusHostWindow()
    {
        // Only the macOS head places native focus on hosted WKWebViews; the interop is a no-op elsewhere,
        // where there is nothing to resign.
        MacOSWindowInterop.MakeContentViewFirstResponder();
    }
}
