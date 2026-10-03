using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Automation.Platform;

/// <summary>
/// Registers the Automation services whose implementation is selected per platform.
/// </summary>
public static class PlatformServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        // The Windows App SDK draws the caption buttons on the packaged Windows head. AppKit draws the menu bar,
        // the window's buttons and the web views on macOS. The Skia head on Windows reads nothing natively.
#if WINDOWS
        services.AddSingleton<INativeControlReader, WindowsNativeControlReader>();
#else
        if (OperatingSystem.IsMacOS())
        {
            services.AddSingleton<INativeControlReader, MacOSNativeControlReader>();
        }
        else
        {
            services.AddSingleton<INativeControlReader, NullNativeControlReader>();
        }
#endif
    }
}
