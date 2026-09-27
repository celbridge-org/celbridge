using Celbridge.Host;

namespace Celbridge.WebHost;

/// <summary>
/// The host/log RPC target for one surface. It reads the surface's name for each entry, so a renamed document
/// logs under its new name.
/// </summary>
public sealed class WebSurfaceLogTarget : IHostLog
{
    private readonly Func<string> _getSurfaceName;
    private readonly IWebSurfaceLog _webSurfaceLog;

    public WebSurfaceLogTarget(Func<string> getSurfaceName, IWebSurfaceLog webSurfaceLog)
    {
        _getSurfaceName = getSurfaceName;
        _webSurfaceLog = webSurfaceLog;
    }

    public void OnLog(string? level, string? message)
    {
        _webSurfaceLog.Write(_getSurfaceName(), level, message);
    }
}
