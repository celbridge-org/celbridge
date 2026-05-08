using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Server.Services;

/// <summary>
/// Builds the call-tool filter that enforces the cold-start gate and records
/// per-invocation telemetry. The filter wraps every tools/call dispatch:
///
/// <list type="bullet">
///   <item>Bootstrap tools (guides_list, guides_read, guides_search) always pass.</item>
///   <item>Proxy connections (clientInfo.name = CelbridgeMcpToolBridge) bypass
///     the gate so Python and JavaScript proxy callers don't need to read
///     agent_instructions before using tools.</item>
///   <item>For agent connections, non-bootstrap tools fail with a synthetic
///     ToolError until guides_read has been called for "agent_instructions" on
///     this session.</item>
///   <item>Every dispatch is recorded as a ToolInvocationRecord on
///     ToolTelemetry, including duration, payload sizes, and the cache-miss
///     flag (whether the agent had read this tool's per-tool guide on this
///     session before invoking it).</item>
/// </list>
///
/// guides_read invocations are inspected after the inner handler runs so that
/// the orientation flag and the per-session read-guides set track what the
/// agent actually requested. Side effects only fire on a successful response —
/// a failed guides_read (bad JSON arguments, missing names parameter) does not
/// flip orientation or mark guides as read. Names that resolve to the response's
/// "unknown" array still count as read for telemetry purposes; the orientation
/// flag's exact-match on "agent_instructions" means it can only be flipped by
/// passing that literal name.
/// </summary>
internal static class ToolGate
{
    public static McpRequestFilter<CallToolRequestParams, CallToolResult> CreateFilter(ToolTelemetry telemetry)
    {
        return next => (context, cancellationToken) => InvokeAsync(telemetry, next, context, cancellationToken);
    }

    private static async ValueTask<CallToolResult> InvokeAsync(
        ToolTelemetry telemetry,
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
    {
        var server = context.Server;
        var toolName = context.Params?.Name ?? "";
        var session = telemetry.GetOrCreateSession(server);
        if (session is null)
        {
            // No MCP session id available (stateless transport or pre-initialize).
            // Skip the gate and skip telemetry rather than synthesise a fake session;
            // the broker's stateful Streamable HTTP transport always populates
            // SessionId before tools/call, so this branch shouldn't fire in practice.
            return await next(context, cancellationToken).ConfigureAwait(false);
        }

        var arguments = context.Params?.Arguments;
        var argBytes = MeasureArguments(arguments);
        var isBootstrap = telemetry.IsBootstrapTool(toolName);

        if (!isBootstrap && !telemetry.IsOrientationSatisfied(session))
        {
            var blocked = BuildOrientationGateError();
            telemetry.RecordInvocation(server, session, toolName, new InvocationOutcome(
                Success: false,
                ErrorMessage: ExtractTextContent(blocked),
                DurationMilliseconds: 0,
                ArgPayloadBytes: argBytes,
                ResultPayloadBytes: MeasureResult(blocked)));
            return blocked;
        }

        var stopwatch = Stopwatch.StartNew();
        CallToolResult result;
        try
        {
            result = await next(context, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            stopwatch.Stop();
            telemetry.RecordInvocation(server, session, toolName, new InvocationOutcome(
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
            ApplyGuidesReadSideEffects(telemetry, session, arguments);
        }

        var errorMessage = success ? "" : ExtractTextContent(result);
        telemetry.RecordInvocation(server, session, toolName, new InvocationOutcome(
            Success: success,
            ErrorMessage: errorMessage,
            DurationMilliseconds: stopwatch.Elapsed.TotalMilliseconds,
            ArgPayloadBytes: argBytes,
            ResultPayloadBytes: MeasureResult(result)));

        return result;
    }

    internal static void ApplyGuidesReadSideEffects(
        ToolTelemetry telemetry,
        ToolSessionState session,
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
            telemetry.MarkGuideRead(session, guideName);
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

    private static CallToolResult BuildOrientationGateError()
    {
        var message =
            "This is a fresh session. Before using other tools, call the `guides_read` tool with " +
            "`names: [\"agent_instructions\"]` on its own — do not parallelize it with other tool calls " +
            "in the same turn, or those calls will be rejected by this same gate. That guide covers " +
            "essential conventions and points to namespace guides for each tool domain.";
        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = message }],
        };
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
