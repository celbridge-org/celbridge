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
    /// Prepends agent_instructions, the namespace guide, and the per-tool
    /// guide to the result for any that haven't been served on this session
    /// yet. Returns the result unchanged for proxy connections.
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

        var prefix = new List<ContentBlock>();

        if (_monitor.TryMarkServed(session, "agent_instructions"))
        {
            var body = GetGuideBody("agent_instructions");
            if (!string.IsNullOrEmpty(body))
            {
                prefix.Add(new TextContentBlock { Text = body });
            }
        }

        var namespaceName = ExtractNamespace(toolName);
        if (!string.IsNullOrEmpty(namespaceName)
            && _monitor.TryMarkServed(session, namespaceName))
        {
            var body = GetGuideBody(namespaceName);
            if (!string.IsNullOrEmpty(body))
            {
                prefix.Add(new TextContentBlock { Text = body });
            }
        }

        if (_monitor.TryMarkServed(session, toolName))
        {
            var body = GetGuideBody(toolName);
            if (!string.IsNullOrEmpty(body))
            {
                prefix.Add(new TextContentBlock { Text = body });
            }
        }

        if (prefix.Count == 0)
        {
            return result;
        }

        // Single ordering rule: when guides attach, all guide blocks come
        // before the original result content. Preserves IsError so error
        // responses still surface as errors with the guides preceding them.
        var combined = new List<ContentBlock>(prefix.Count + (result.Content?.Count ?? 0));
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
        };
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
