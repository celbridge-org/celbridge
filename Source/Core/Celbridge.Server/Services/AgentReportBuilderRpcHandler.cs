using StreamJsonRpc;

namespace Celbridge.Server.Services;

/// <summary>
/// Thin JSON-RPC adapter that exposes <see cref="AgentReportBuilder"/> methods
/// to TCP clients. Forwarding only — all aggregation and report-building
/// logic lives on AgentReportBuilder.
/// </summary>
public class AgentReportBuilderRpcHandler
{
    private readonly AgentReportBuilder _reportBuilder;

    public AgentReportBuilderRpcHandler(AgentReportBuilder reportBuilder)
    {
        _reportBuilder = reportBuilder;
    }

    [JsonRpcMethod("diagnostics/get_agent_report")]
    public Task<string> GetAgentReport()
    {
        return _reportBuilder.GenerateAsync();
    }
}
