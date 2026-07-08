using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// Registers the WebHost services whose implementation is selected per platform.
/// </summary>
public static class PlatformServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        // The WebView adapter is chosen at compile time, not by a runtime OS check: the packaged Windows head
        // drives the WebView2 SDK directly, while every Uno Skia head (desktop Windows and macOS) falls back to
        // ExecuteScriptAsync and the native WKWebView interop. A runtime check could not tell the packaged head
        // from the desktop Windows head, which need different adapters.
#if WINDOWS
        services.AddSingleton<IWebViewAdapter, WindowsWebViewAdapter>();
#else
        services.AddSingleton<IWebViewAdapter, SkiaWebViewAdapter>();
#endif

        // The focus monitor is a genuine runtime OS selection: the AppKit first-responder signal
        // exists only on macOS, which is also the only OS where WKWebView consumes clicks without
        // raising the managed GotFocus event.
        if (OperatingSystem.IsMacOS())
        {
            services.AddSingleton<IWebViewFocusMonitor, MacOSWebViewFocusMonitor>();
        }
        else
        {
            services.AddSingleton<IWebViewFocusMonitor, NullWebViewFocusMonitor>();
        }
    }
}
