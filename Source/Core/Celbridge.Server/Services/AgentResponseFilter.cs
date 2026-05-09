using System.Diagnostics;
using System.Text.Json;
using Celbridge.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Server.Services;

/// <summary>
/// Wraps every MCP tools/call dispatch to record per-invocation data on
/// AgentMonitor and auto-attach guide bodies on first use per session.
/// Proxy connections receive the bare result.
/// </summary>
internal sealed class AgentResponseFilter
{
    private readonly AgentMonitor _monitor;
    private readonly IGuides _guides;

    public AgentResponseFilter(AgentMonitor monitor, IGuides guides)
    {
        _monitor = monitor;
        _guides = guides;
    }

    public McpRequestFilter<CallToolRequestParams, CallToolResult> CreateFilter()
    {
        return next => (context, cancellationToken) => InvokeAsync(next, context, cancellationToken);
    }

    private async ValueTask<CallToolResult> InvokeAsync(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
    {
        var server = context.Server;
        var toolName = context.Params?.Name ?? "";
        var session = _monitor.GetOrCreateSession(server);
        if (session is null)
        {
            // No MCP session id available (stateless transport or pre-initialize).
            // Skip auto-attach and skip monitoring rather than synthesise a fake
            // session; the broker's stateful Streamable HTTP transport always
            // populates SessionId before tools/call, so this branch shouldn't
            // fire in practice.
            return await next(context, cancellationToken).ConfigureAwait(false);
        }

        var arguments = context.Params?.Arguments;
        var argBytes = MeasureArguments(arguments);

        var stopwatch = Stopwatch.StartNew();
        CallToolResult result;
        try
        {
            result = await next(context, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            stopwatch.Stop();
            _monitor.RecordInvocation(server, session, toolName, new InvocationOutcome(
                Success: false,
                ErrorMessage: "<exception>",
                DurationMilliseconds: stopwatch.Elapsed.TotalMilliseconds,
                ArgPayloadBytes: argBytes,
                ResultPayloadBytes: 0));
            throw;
        }
        stopwatch.Stop();

        var success = result.IsError != true;
        if (toolName == "guides_read"
            && success)
        {
            ApplyGuidesReadSideEffects(_monitor, session, arguments);
        }

        result = ApplyAutoAttach(result, session, toolName);

        var errorMessage = success ? "" : ExtractTextContent(result);
        _monitor.RecordInvocation(server, session, toolName, new InvocationOutcome(
            Success: success,
            ErrorMessage: errorMessage,
            DurationMilliseconds: stopwatch.Elapsed.TotalMilliseconds,
            ArgPayloadBytes: argBytes,
            ResultPayloadBytes: MeasureResult(result)));

        return result;
    }

    /// <summary>
    /// Builds the candidate list of guides for this call and prepends the body
    /// of each candidate that hasn't been served on this session yet. The
    /// candidate list is broadest-scoped first: orientation, namespace,
    /// per-tool, then [RelatedGuides] in declaration order, and finally any
    /// troubleshooter named in the result's Meta dictionary. The
    /// troubleshooter Meta entry is removed before the response leaves the
    /// broker. Returns the result unchanged for proxy connections.
    /// </summary>
    internal CallToolResult ApplyAutoAttach(CallToolResult result, AgentSessionState session, string toolName)
    {
        if (session.IsProxyClient)
        {
            return result;
        }

        if (string.IsNullOrEmpty(toolName))
        {
            return result;
        }

        var troubleshooterName = ExtractAndClearTroubleshooterMeta(result);
        var troubleshooterNames = troubleshooterName is null
            ? Array.Empty<string>()
            : new[] { troubleshooterName };

        var candidates = BuildCandidateList(toolName, troubleshooterNames);
        var prefix = new List<ContentBlock>();
        foreach (var candidateName in candidates)
        {
            if (!_monitor.TryMarkServed(session, candidateName))
            {
                continue;
            }
            var body = GetGuideBody(candidateName);
            if (!string.IsNullOrEmpty(body))
            {
                prefix.Add(new TextContentBlock { Text = body });
            }
        }

        if (prefix.Count == 0)
        {
            return result;
        }

        var combinedCount = prefix.Count + (result.Content?.Count ?? 0);
        var combined = new List<ContentBlock>(combinedCount);
        combined.AddRange(prefix);
        if (result.Content is not null)
        {
            combined.AddRange(result.Content);
        }

        return new CallToolResult
        {
            IsError = result.IsError,
            Content = combined,
            StructuredContent = result.StructuredContent,
            Meta = result.Meta,
        };
    }

    /// <summary>
    /// Builds the broadest-first candidate list for the given tool call:
    /// agent_instructions, namespace, tool name, declared [RelatedGuides]
    /// (in declaration order), then troubleshooter names supplied by the
    /// caller (most specific). Order matters: the walker attaches in this
    /// order, and TryMarkServed dedups across the walk.
    /// </summary>
    internal IReadOnlyList<string> BuildCandidateList(string toolName, IReadOnlyList<string> troubleshooterNames)
    {
        var candidates = new List<string>(8);
        candidates.Add("agent_instructions");

        var namespaceName = ExtractNamespace(toolName);
        if (!string.IsNullOrEmpty(namespaceName))
        {
            candidates.Add(namespaceName);
        }

        candidates.Add(toolName);

        foreach (var relatedName in _guides.GetRelatedGuides(toolName))
        {
            candidates.Add(relatedName);
        }

        foreach (var troubleshooterName in troubleshooterNames)
        {
            candidates.Add(troubleshooterName);
        }

        return candidates;
    }

    /// <summary>
    /// Reads the troubleshooter name out of the result's Meta dictionary (set
    /// by ToolResponse category helpers) and clears the entry so it doesn't
    /// leak to the agent. Returns null when no troubleshooter was named.
    /// </summary>
    internal static string? ExtractAndClearTroubleshooterMeta(CallToolResult result)
    {
        var meta = result.Meta;
        if (meta is null)
        {
            return null;
        }
        if (!meta.TryGetPropertyValue(ToolResponse.TroubleshooterMetaKey, out var node)
            || node is null)
        {
            return null;
        }

        var troubleshooterName = node.GetValue<string>();
        meta.Remove(ToolResponse.TroubleshooterMetaKey);
        return troubleshooterName;
    }

    /// <summary>
    /// Returns the body of the named guide, or the empty string when the name
    /// is unknown.
    /// </summary>
    private string GetGuideBody(string guideName)
    {
        var entry = _guides.GetByName(guideName);
        return entry?.Body ?? "";
    }

    /// <summary>
    /// Returns the namespace prefix of a tool alias (everything before the
    /// first underscore), or the empty string when the name has no underscore.
    /// </summary>
    private static string ExtractNamespace(string toolName)
    {
        var underscoreIndex = toolName.IndexOf('_');
        if (underscoreIndex <= 0)
        {
            return "";
        }
        return toolName.Substring(0, underscoreIndex);
    }

    internal static void ApplyGuidesReadSideEffects(
        AgentMonitor monitor,
        AgentSessionState session,
        IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null
            || !arguments.TryGetValue("names", out var namesElement))
        {
            return;
        }

        var requested = ParseRequestedGuideNames(namesElement);
        foreach (var guideName in requested)
        {
            monitor.MarkGuideRead(session, guideName);
        }
    }

    internal static List<string> ParseRequestedGuideNames(JsonElement namesElement)
    {
        var result = new List<string>();
        var json = namesElement.ValueKind == JsonValueKind.String
            ? namesElement.GetString() ?? ""
            : namesElement.GetRawText();
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(json);
            if (parsed is not null)
            {
                foreach (var name in parsed)
                {
                    if (!string.IsNullOrEmpty(name))
                    {
                        result.Add(name);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Malformed names argument; the inner tool already produced an error
            // result, so we just skip the side-effects.
        }

        return result;
    }

    private static long MeasureArguments(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return 0;
        }

        long total = 0;
        foreach (var pair in arguments)
        {
            total += pair.Key.Length;
            total += pair.Value.GetRawText().Length;
        }
        return total;
    }

    private static long MeasureResult(CallToolResult result)
    {
        long total = 0;
        if (result.Content is not null)
        {
            foreach (var block in result.Content)
            {
                if (block is TextContentBlock textBlock)
                {
                    total += textBlock.Text?.Length ?? 0;
                }
            }
        }
        return total;
    }

    private static string ExtractTextContent(CallToolResult result)
    {
        if (result.Content is null)
        {
            return "";
        }
        foreach (var block in result.Content)
        {
            if (block is TextContentBlock textBlock && !string.IsNullOrEmpty(textBlock.Text))
            {
                return textBlock.Text;
            }
        }
        return "";
    }
}
