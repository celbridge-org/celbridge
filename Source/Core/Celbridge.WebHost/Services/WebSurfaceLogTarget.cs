using Celbridge.Host;

namespace Celbridge.WebHost;

/// <summary>
/// The host/log RPC target for one web view.
/// </summary>
public sealed class WebSurfaceLogTarget : IHostLog
{
    private readonly IWebView _view;
    private readonly IWebSurfaceLog _webSurfaceLog;

    public WebSurfaceLogTarget(IWebView view, IWebSurfaceLog webSurfaceLog)
    {
        _view = view;
        _webSurfaceLog = webSurfaceLog;
    }

    public void OnLog(string? level, string? message)
    {
        _webSurfaceLog.Write(_view, level, message);
    }
}
