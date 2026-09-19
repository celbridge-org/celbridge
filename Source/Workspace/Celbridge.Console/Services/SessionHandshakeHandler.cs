using StreamJsonRpc;

namespace Celbridge.Console.Services;

/// <summary>
/// Per-connection RPC target that binds an inbound connection to the console session whose token it echoes.
/// One instance is created per transport connection with that connection's id, so the registry can map the
/// connection to the console that launched it.
/// </summary>
internal sealed class SessionHandshakeHandler
{
    private readonly IConsoleSessionService _sessionService;
    private readonly int _connectionId;

    public SessionHandshakeHandler(IConsoleSessionService sessionService, int connectionId)
    {
        _sessionService = sessionService;
        _connectionId = connectionId;
    }

    // A client that runs in a temporary environment also reports it. The folder is optional, so a client
    // that reports none still binds.
    [JsonRpcMethod("session/handshake")]
    public bool Handshake(string sessionToken, string? temporaryEnvironmentFolder = null)
    {
        if (!Guid.TryParse(sessionToken, out var token))
        {
            return false;
        }

        return _sessionService.TryBindConnection(token, _connectionId, temporaryEnvironmentFolder);
    }
}
