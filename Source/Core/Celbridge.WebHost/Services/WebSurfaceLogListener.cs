using Celbridge.Host;

namespace Celbridge.WebHost;

/// <summary>
/// The native web message bus entrance to the diagnostic plane, the counterpart to the WebSurfaceLogTarget
/// that serves pages holding an RPC channel. A page can be left without a client library, and the scripts the
/// host injects run before one exists, so the same host/log notification has to be readable off the bus.
/// </summary>
internal sealed class WebSurfaceLogListener
{
    private readonly IWebSurfaceLog _webSurfaceLog;

    public WebSurfaceLogListener(IWebSurfaceLog webSurfaceLog)
    {
        _webSurfaceLog = webSurfaceLog;
    }

    public void Start(IWebSurfaceMessageDispatcher messageDispatcher)
    {
        messageDispatcher.AddHandler(LogRpcMethods.Log, OnLog);
    }

    private void OnLog(WebSurfaceMessage message)
    {
        _webSurfaceLog.Write(
            message.SurfaceName,
            WebMessageEnvelope.ReadString(message.Parameters, "level"),
            WebMessageEnvelope.ReadString(message.Parameters, "message"));
    }
}
