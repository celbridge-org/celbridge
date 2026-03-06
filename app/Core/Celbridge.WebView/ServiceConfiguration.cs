using Celbridge.Core;
using Celbridge.WebView.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.WebView;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IWebViewFactory, WebViewFactory>();
    }

    /// <summary>
    /// Initialize the WebView services.
    /// This forces the WebViewFactory to be instantiated early, allowing it to 
    /// pre-warm the WebView2 pool in the background while the application starts up.
    /// </summary>
    public static void Initialize()
    {
        // Force early instantiation of WebViewFactory to start pre-warming the WebView2 pool
        var webViewFactory = ServiceLocator.AcquireService<IWebViewFactory>();
        Guard.IsNotNull(webViewFactory);
    }
}
