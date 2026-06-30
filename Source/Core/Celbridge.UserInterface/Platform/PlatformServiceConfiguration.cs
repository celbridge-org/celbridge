using Celbridge.UserInterface.Helpers.FullScreen;
using Celbridge.UserInterface.Platform.FullScreen;

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
    }
}
