using Celbridge.Logging;

namespace Celbridge.UserInterface.Helpers.FullScreen;

/// <summary>
/// Fullscreen controller for the macOS Skia desktop head. The monitor-cover hook is not yet
/// implemented, so it falls back to the base borderless maximize; the macOS port should cover the
/// screen with native NSScreen/NSWindow handling (and account for the menu bar and Dock).
/// </summary>
public sealed class MacDesktopFullScreenController : DesktopFullScreenControllerBase
{
    public MacDesktopFullScreenController(ILogger<MacDesktopFullScreenController> logger)
        : base(logger)
    {
    }

    protected override bool TryCoverMonitor()
    {
        Logger.LogDebug("Monitor cover is not implemented for the macOS desktop head; using borderless maximize");
        return false;
    }

    protected override void ReleaseMonitorCover()
    {
    }
}
