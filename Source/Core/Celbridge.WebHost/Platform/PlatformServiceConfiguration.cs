using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// Registers the WebHost services whose implementation is selected per platform.
/// </summary>
public static class PlatformServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        // The web view platform is chosen at compile time. The packaged Windows head drives the WebView2 SDK
        // directly. The Uno Skia heads, desktop Windows and macOS, fall back to ExecuteScriptAsync and the native
        // WKWebView interop. A runtime OS check could not tell the packaged head from the desktop Windows head.
#if WINDOWS
        services.AddSingleton<IWebViewPlatform, WindowsWebViewPlatform>();
#else
        services.AddSingleton<IWebViewPlatform, SkiaWebViewPlatform>();
#endif
    }
}
