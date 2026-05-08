using System.Collections.Concurrent;
using ModelContextProtocol.Server;

namespace Celbridge.Server.Services;

/// <summary>
/// One captured tool invocation. Field order is the column order used in the
/// Invocations sheet of the workbook produced by AgentAnalytics.GenerateAsync.
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
/// Outcome data observed by the tool-call filter for a single invocation.
/// Bundles the dynamic fields of a captured row that aren't already known
/// from the session or the tool name, so the filter can hand them across
/// without an 8-parameter call.
/// </summary>
internal record class InvocationOutcome(
    bool Success,
    string ErrorMessage,
    double DurationMilliseconds,
    long ArgPayloadBytes,
    long ResultPayloadBytes);

/// <summary>
/// Per-connection state owned by ToolTelemetry. Tracks the orientation gate flag,
/// the set of guide names already read in the session (used to compute the
/// cache-miss flag for tool invocations), and the MCP session id (carried into
/// invocation rows so they correlate with the Mcp-Session-Id header observed by
/// the client).
/// </summary>
internal sealed class ToolSessionState
{
    public ToolSessionState(string sessionId)
    {
        SessionId = sessionId;
    }

    public string SessionId { get; }
    public bool OrientationRead { get; set; }
    public HashSet<string> GuidesReadThisSession { get; } = new(StringComparer.Ordinal);
    public bool IsProxyClient { get; set; }
}

/// <summary>
/// Collects per-invocation tool telemetry and per-connection session state for the
/// MCP server. Per-session entries are keyed on McpServer.SessionId — the stable
/// identifier the SDK assigns at MCP initialize time and propagates through the
/// Mcp-Session-Id header on every subsequent request. Invocation rows are the
/// source of truth for the telemetry report; the report writer projects them
/// directly to the workbook.
/// </summary>
public sealed class ToolTelemetry
{
    /// <summary>
    /// Maximum number of invocation rows kept in memory. Older rows are evicted
    /// FIFO when the cap is hit so a long REPL session doesn't grow unbounded.
    /// </summary>
    private const int MaxInvocationRows = 5_000;

    /// <summary>
    /// Client info name used by McpToolBridge when it connects internally on
    /// behalf of the Python and JavaScript proxies. Connections with this name
    /// bypass the cold-start gate and have no orientation requirement.
    /// </summary>
    public const string ProxyClientName = "CelbridgeMcpToolBridge";

    private readonly ConcurrentDictionary<string, ToolSessionState> _sessionStates = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<ToolInvocationRecord> _invocations = new();
    private long _invocationCount;

    /// <summary>
    /// Returns the per-session state for the given MCP server, or null if the
    /// server has no SessionId yet (stateless transport or pre-initialize). The
    /// gate filter treats a null return as "let the call through, skip
    /// telemetry" — the broker's stateful Streamable HTTP transport always
    /// populates SessionId before the first tools/call, so this branch is
    /// defence-in-depth against an unexpected transport configuration.
    /// </summary>
    internal ToolSessionState? GetOrCreateSession(McpServer server)
    {
        var sessionId = server.SessionId;
        if (string.IsNullOrEmpty(sessionId))
        {
            return null;
        }

        var state = _sessionStates.GetOrAdd(sessionId, key => new ToolSessionState(key));
        var clientName = server.ClientInfo?.Name ?? "";
        state.IsProxyClient = string.Equals(clientName, ProxyClientName, StringComparison.Ordinal);
        return state;
    }

    public bool IsBootstrapTool(string toolName)
    {
        return toolName switch
        {
            "guides_list" or "guides_read" or "guides_search" => true,
            _ => false,
        };
    }

    /// <summary>
    /// Returns true when the connection has either read the orientation guide on
    /// this session or is a proxy connection that bypasses the gate. Bootstrap
    /// tools are allowed unconditionally; callers should check IsBootstrapTool
    /// before consulting this method.
    /// </summary>
    internal bool IsOrientationSatisfied(ToolSessionState state)
    {
        return state.IsProxyClient || state.OrientationRead;
    }

    internal void MarkOrientationRead(ToolSessionState state)
    {
        state.OrientationRead = true;
    }

    internal void MarkGuideRead(ToolSessionState state, string guideName)
    {
        state.GuidesReadThisSession.Add(guideName);
        if (string.Equals(guideName, "agent_instructions", StringComparison.Ordinal))
        {
            state.OrientationRead = true;
        }
    }

    internal bool WasGuideReadInSession(ToolSessionState state, string guideName)
    {
        return state.GuidesReadThisSession.Contains(guideName);
    }

    public void RecordInvocation(ToolInvocationRecord record)
    {
        _invocations.Enqueue(record);
        Interlocked.Increment(ref _invocationCount);

        while (_invocations.Count > MaxInvocationRows && _invocations.TryDequeue(out _))
        {
        }
    }

    /// <summary>
    /// Builds a ToolInvocationRecord from the filter's observation context and
    /// records it. Owns the cache-miss policy (non-bootstrap, non-proxy, no
    /// per-tool guide read this session) and the clientInfo extraction so the
    /// gate filter doesn't have to know about row shape or telemetry policy.
    /// </summary>
    internal void RecordInvocation(
        McpServer server,
        ToolSessionState session,
        string toolName,
        InvocationOutcome outcome)
    {
        var clientInfo = server.ClientInfo;
        var clientName = clientInfo?.Name ?? "";
        var clientVersion = clientInfo?.Version ?? "";

        // Cache-miss is only meaningful for non-bootstrap, non-proxy invocations:
        // bootstrap tools have no associated guide, and proxy callers are not
        // expected to read guides before invoking.
        var isBootstrap = IsBootstrapTool(toolName);
        var cacheMiss = !isBootstrap
            && !session.IsProxyClient
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
    /// All captured invocation rows in observation order. Stable shape suitable
    /// for direct projection to a flat-row export.
    /// </summary>
    public IReadOnlyList<ToolInvocationRecord> Invocations => _invocations.ToArray();
}
