using Celbridge.WebHost.Commands;
using Celbridge.WebHost.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.WebHost;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IWebViewService, WebViewService>();
        services.AddSingleton<IWebViewFactory, WebViewFactory>();
        services.AddSingleton<IWebViewFocusRegistry, WebViewFocusRegistry>();
        services.AddSingleton<IDocumentWebViewToolBridge, DocumentWebViewToolBridge>();
        services.AddTransient<IGetWebViewToolSupportCommand, GetWebViewToolSupportCommand>();

        // The loopback default is registered first, so it is the fallback loader. A module may register a
        // custom loader, which resolves ahead of it (the view picks the last matching loader).
        services.AddSingleton<ICustomEditorLoader, LoopbackCustomEditorLoader>();

        // The per-platform WebView adapter (WebView2 SDK on Windows, Skia/native fallbacks elsewhere).
        Platform.PlatformServiceConfiguration.ConfigureServices(services);
    }

    /// <summary>
    /// Instantiates the WebViewFactory early so it can pre-warm the WebView2 pool in the background while
    /// the application starts up.
    /// </summary>
    public static void Initialize()
    {
        // Force early instantiation of WebViewFactory to start pre-warming the WebView2 pool
        var webViewFactory = ServiceLocator.AcquireService<IWebViewFactory>();
        Guard.IsNotNull(webViewFactory);
    }
}
