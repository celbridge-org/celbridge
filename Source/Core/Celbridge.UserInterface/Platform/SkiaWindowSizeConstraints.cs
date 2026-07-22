using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Minimum window size for the Skia desktop heads. On macOS the constraint is set on the native window,
/// where AppKit enforces it. The other Skia heads expose no equivalent mechanism, so the window is left
/// unconstrained there.
/// </summary>
public sealed class SkiaWindowSizeConstraints : IWindowSizeConstraints
{
    public void ApplyMinimumSize(AppWindow appWindow, SizeInt32 minimumSize)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        // macOS points match the size units used elsewhere in the window state, so no scaling is needed.
        MacOSWindowInterop.SetMinimumContentSize(minimumSize.Width, minimumSize.Height);
    }
}
