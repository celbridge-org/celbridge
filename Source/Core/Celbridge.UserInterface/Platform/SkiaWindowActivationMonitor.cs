using Celbridge.UserInterface.Services;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// No-op window activation monitor for the Skia desktop heads, which draw a native title bar that the OS
/// tints on activation changes.
/// </summary>
internal sealed class SkiaWindowActivationMonitor : IWindowActivationMonitor
{
    public void Start(Window window)
    {
        // The native title bar is tinted by the OS, so there is nothing to monitor here.
    }
}
