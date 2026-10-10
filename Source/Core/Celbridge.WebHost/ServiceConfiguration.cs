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
        services.AddSingleton<IWebSurfaceLog, WebSurfaceLog>();
        services.AddSingleton<WebSurfaceLogListener>();
        services.AddSingleton<IWebSurfaceMessageDispatcher, WebSurfaceMessageDispatcher>();
        services.AddSingleton<IWebViewFocusRegistry, WebViewFocusRegistry>();
        services.AddSingleton<IWebViewToolBridge, WebViewToolBridge>();
        services.AddTransient<IGetWebViewToolSupportCommand, GetWebViewToolSupportCommand>();
        services.AddTransient<IClearBrowsingDataCommand, ClearBrowsingDataCommand>();

        // The loopback default is registered first, so it is the fallback loader. A module may register a
        // custom loader, which resolves ahead of it (the view picks the last matching loader).
        services.AddSingleton<ICustomEditorLoader, LoopbackCustomEditorLoader>();

        Platform.PlatformServiceConfiguration.ConfigureServices(services);
    }

    /// <summary>
    /// Instantiates the WebViewFactory early, so it prewarms web views in the background while the application
    /// starts up. Also opens the native message bus entrance to the diagnostic plane.
    /// </summary>
    public static void Initialize()
    {
        // Force early instantiation of WebViewFactory to start prewarming web views
        var webViewFactory = ServiceLocator.AcquireService<IWebViewFactory>();
        Guard.IsNotNull(webViewFactory);

        // Registered before any surface attaches, so no page's diagnostics are dropped for want of a handler.
        var messageDispatcher = ServiceLocator.AcquireService<IWebSurfaceMessageDispatcher>();
        var logListener = ServiceLocator.AcquireService<WebSurfaceLogListener>();
        logListener.Start(messageDispatcher);
    }
}
