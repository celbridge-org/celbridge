using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Automation.Platform;

/// <summary>
/// Registers the Automation services whose implementation is selected per platform.
/// </summary>
public static class PlatformServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        // AppKit draws the menu bar, the window's buttons and the web views on macOS.
        if (OperatingSystem.IsMacOS())
        {
            services.AddSingleton<INativeControlReader, MacOSNativeControlReader>();
        }
        else
        {
            services.AddSingleton<INativeControlReader, NullNativeControlReader>();
        }
    }
}
