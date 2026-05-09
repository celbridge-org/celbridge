using System.Collections.Concurrent;
using Celbridge.Tools;
using ModelContextProtocol.Server;

namespace Celbridge.Server.Services;

/// <summary>
/// One captured tool invocation. Field order matches the Invocations workbook
/// column order.
/// </summary>
public record class ToolInvocationRecord(
    DateTimeOffset TimestampUtc,
    string SessionId,
    string ClientName,
    string ClientVersion,
    string ToolName,
    bool Success,
    string ErrorMessage,
    double DurationMilliseconds,
    long ArgPayloadBytes,
    long ResultPayloadBytes,
    bool ProxyClient,
    bool CacheMiss);

/// <summary>
/// Outcome fields the response filter observes for a single invocation.
/// </summary>
internal record class InvocationOutcome(
    bool Success,
    string ErrorMessage,
    double DurationMilliseconds,
    long ArgPayloadBytes,
    long ResultPayloadBytes);

/// <summary>
/// Per-MCP-session state owned by AgentMonitor: the served-guides set and the
/// session id. Mutating members are safe to call concurrently.
/// </summary>
internal sealed class AgentSessionState
{
    // ConcurrentDictionary used as a set. HashSet<string> is not safe under
    // concurrent Add and Contains, which happens when an agent issues parallel
    // tool calls in the same turn.
    private readonly ConcurrentDictionary<string, byte> _guidesServed = new(StringComparer.Ordinal);

    public AgentSessionState(string sessionId)
    {
        SessionId = sessionId;
    }

    public string SessionId { get; }

    public bool IsProxyClient { get; set; }

    /// <summary>
    /// Atomic check-and-set on the served-guides set. Returns true on first
    /// observation of the name, false thereafter.
    /// </summary>
    public bool TryMarkServed(string guideName)
    {
        return _guidesServed.TryAdd(guideName, 0);
    }

    public void MarkGuideRead(string guideName)
    {
        _guidesServed.TryAdd(guideName, 0);
    }

    public bool WasGuideRead(string guideName)
    {
        return _guidesServed.ContainsKey(guideName);
    }
}

/// <summary>
/// Records per-invocation MCP tool monitoring data and per-MCP-session state.
/// Invocation rows are the source of truth for the agent report.
/// </summary>
public sealed class AgentMonitor
{
    /// <summary>
    /// Maximum number of invocation rows kept in memory before FIFO eviction.
    /// </summary>
    private const int MaxInvocationRows = 5_000;

    /// <summary>
    /// Client-info name used by McpToolBridge for proxy connections. Identifies
    /// callers that bypass auto-attach.
    /// </summary>
    public const string ProxyClientName = "CelbridgeMcpToolBridge";

    private readonly ConcurrentDictionary<string, AgentSessionState> _sessionStates = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<ToolInvocationRecord> _invocations = new();

    /// <summary>
    /// Returns the per-session state for the given MCP server, or null when
    /// the server has no SessionId yet.
    /// </summary>
    internal AgentSessionState? GetOrCreateSession(McpServer server)
    {
        var sessionId = server.SessionId;
        if (string.IsNullOrEmpty(sessionId))
        {
            return null;
        }

        var state = _sessionStates.GetOrAdd(sessionId, key => new AgentSessionState(key));
        var clientName = server.ClientInfo?.Name ?? "";
        state.IsProxyClient = string.Equals(clientName, ProxyClientName, StringComparison.Ordinal);
        return state;
    }

    /// <summary>
    /// Drops all per-session state. Captured invocation rows are retained so
    /// the agent report can aggregate across the whole application session.
    /// </summary>
    public void ClearSessions()
    {
        _sessionStates.Clear();
    }

    /// <summary>
    /// Atomic check-and-set on the per-session served-guides set; returns
    /// true on first observation.
    /// </summary>
    internal bool TryMarkServed(AgentSessionState state, string guideName)
    {
        return state.TryMarkServed(guideName);
    }

    internal void MarkGuideRead(AgentSessionState state, string guideName)
    {
        state.MarkGuideRead(guideName);
    }

    internal bool WasGuideReadInSession(AgentSessionState state, string guideName)
    {
        return state.WasGuideRead(guideName);
    }

    public void RecordInvocation(ToolInvocationRecord record)
    {
        _invocations.Enqueue(record);

        while (_invocations.Count > MaxInvocationRows && _invocations.TryDequeue(out _))
        {
        }
    }

    /// <summary>
    /// Builds a ToolInvocationRecord from the filter's observation context
    /// and records it.
    /// </summary>
    internal void RecordInvocation(
        McpServer server,
        AgentSessionState session,
        string toolName,
        InvocationOutcome outcome)
    {
        var clientInfo = server.ClientInfo;
        var clientName = clientInfo?.Name ?? "";
        var clientVersion = clientInfo?.Version ?? "";

        // Cache-miss is only meaningful for non-proxy invocations: proxy
        // callers are not expected to read guides before invoking.
        var cacheMiss = !session.IsProxyClient
            && !WasGuideReadInSession(session, toolName);

        var record = new ToolInvocationRecord(
            TimestampUtc: DateTimeOffset.UtcNow,
            SessionId: session.SessionId,
            ClientName: clientName,
            ClientVersion: clientVersion,
            ToolName: toolName,
            Success: outcome.Success,
            ErrorMessage: outcome.ErrorMessage,
            DurationMilliseconds: outcome.DurationMilliseconds,
            ArgPayloadBytes: outcome.ArgPayloadBytes,
            ResultPayloadBytes: outcome.ResultPayloadBytes,
            ProxyClient: session.IsProxyClient,
            CacheMiss: cacheMiss);
        RecordInvocation(record);
    }

    /// <summary>
    /// All captured invocation rows in observation order.
    /// </summary>
    public IReadOnlyList<ToolInvocationRecord> Invocations => _invocations.ToArray();
}
