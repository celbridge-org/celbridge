using Celbridge.UserInterface.Helpers.FullScreen;
using Celbridge.UserInterface.Platform.FullScreen;
using Celbridge.UserInterface.Services;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Registers the UserInterface services whose implementation is selected per platform.
/// </summary>
public static class PlatformServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        // The fullscreen mechanism is platform-specific. The packaged WinAppSDK head uses the native
        // fullscreen presenter; the Skia desktop heads emulate fullscreen because the WPF shell's
        // fullscreen presenter has no visual effect.
#if WINDOWS
        services.AddSingleton<IFullScreenController, WinAppSdkFullScreenController>();
#else
        if (OperatingSystem.IsMacOS())
        {
            services.AddSingleton<IFullScreenController, MacDesktopFullScreenController>();
        }
        else
        {
            services.AddSingleton<IFullScreenController, WindowsDesktopFullScreenController>();
        }
#endif

        // Window activation tinting is only meaningful on the head that draws the custom title bar; the
        // Skia heads draw a native title bar that the OS tints, so they use the no-op monitor.
#if WINDOWS
        services.AddSingleton<IWindowActivationMonitor, WindowActivationMonitor>();
#else
        services.AddSingleton<IWindowActivationMonitor, SkiaWindowActivationMonitor>();
#endif

        // The application toolbar is hosted inside the custom title bar on the packaged Windows head and
        // directly beneath the native title bar on the Skia desktop heads.
#if WINDOWS
        services.AddSingleton<IApplicationToolbarHost, WindowsApplicationToolbarHost>();
#else
        services.AddSingleton<IApplicationToolbarHost, SkiaApplicationToolbarHost>();
#endif
    }
}
