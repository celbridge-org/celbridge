using StreamJsonRpc;

namespace Celbridge.Server.Services;

/// <summary>
/// Thin JSON-RPC adapter that exposes <see cref="AgentAnalytics"/> methods
/// to TCP clients. Forwarding only — all aggregation and report-building
/// logic lives on AgentAnalytics.
/// </summary>
public class AgentAnalyticsRpcHandler
{
    private readonly AgentAnalytics _analytics;

    public AgentAnalyticsRpcHandler(AgentAnalytics analytics)
    {
        _analytics = analytics;
    }

    [JsonRpcMethod("diagnostics/get_agent_report")]
    public Task<string> GetAgentReport()
    {
        return _analytics.GenerateAsync();
    }
}
