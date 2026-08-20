using Celbridge.Host;

namespace Celbridge.WebHost;

/// <summary>
/// The host/log RPC target for one surface, naming it in every entry it records.
/// </summary>
public sealed class WebSurfaceLogTarget : IHostLog
{
    private readonly string _surfaceName;
    private readonly IWebSurfaceLog _webSurfaceLog;

    public WebSurfaceLogTarget(string surfaceName, IWebSurfaceLog webSurfaceLog)
    {
        _surfaceName = surfaceName;
        _webSurfaceLog = webSurfaceLog;
    }

    public void OnLog(string? level, string? message)
    {
        _webSurfaceLog.Write(_surfaceName, level, message);
    }
}
