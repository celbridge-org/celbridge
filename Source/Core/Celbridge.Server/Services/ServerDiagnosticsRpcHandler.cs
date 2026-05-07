using StreamJsonRpc;

namespace Celbridge.Server.Services;

/// <summary>
/// Thin JSON-RPC adapter that exposes selected ServerDiagnostics methods to
/// TCP clients.
/// </summary>
public class ServerDiagnosticsRpcHandler
{
    private readonly ServerDiagnostics _diagnostics;

    public ServerDiagnosticsRpcHandler(ServerDiagnostics diagnostics)
    {
        _diagnostics = diagnostics;
    }

    [JsonRpcMethod("diagnostics/get_payload_report")]
    public Task<string> GetPayloadReport()
    {
        return _diagnostics.GeneratePayloadReportAsync();
    }
}
