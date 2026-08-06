using Celbridge.UserInterface.Services;

namespace Celbridge.UserInterface.Platform;

internal sealed class HostWindowFocus : IHostWindowFocus
{
    public void FocusHostWindow()
    {
        // Only the macOS head places native focus on hosted WKWebViews; the other heads have nothing
        // to resign.
        if (OperatingSystem.IsMacOS())
        {
            MacOSWindowInterop.MakeContentViewFirstResponder();
        }
    }
}
