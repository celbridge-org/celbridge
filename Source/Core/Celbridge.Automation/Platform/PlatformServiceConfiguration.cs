using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Automation.Platform;

/// <summary>
/// Registers the Automation services whose implementation is selected per platform.
/// </summary>
public static class PlatformServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        // On the packaged Windows head, the reader reports the caption buttons that the Windows App SDK draws. On
        // macOS, it reports the menu bar, window buttons and web views that AppKit draws. The Skia head on Windows
        // has nothing native to report.
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
