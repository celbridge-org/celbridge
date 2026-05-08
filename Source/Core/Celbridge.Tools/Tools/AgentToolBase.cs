using System.Text.Json;
using Celbridge.Commands;
using ModelContextProtocol.Protocol;

namespace Celbridge.Tools;

/// <summary>
/// Base class for MCP tool classes. Provides access to the main application's
/// services via IApplicationServiceProvider and convenience properties for
/// commonly used services.
/// </summary>
public abstract class AgentToolBase
{
    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IApplicationServiceProvider _services;
    private readonly ICommandService _commandService;

    protected AgentToolBase(IApplicationServiceProvider services)
    {
        _services = services;
        _commandService = services.GetRequiredService<ICommandService>();
    }

    protected T GetRequiredService<T>() where T : class
    {
        return _services.GetRequiredService<T>();
    }

    protected ICommandService CommandService => _commandService;

    /// <summary>
    /// Maximum length, in characters, of an agent-visible error message produced
    /// by a failed Result. Long messages are truncated at the tail with an
    /// ellipsis so the outer-first wrapper survives. Guards against pathological
    /// exception messages from third-party libraries.
    /// </summary>
    private const int MaxErrorMessageLength = 1000;

    /// <summary>
    /// Appended to every agent-visible error message produced by ToolError so the
    /// agent knows where to find the tool's full guide. Bootstrap tools that
    /// document themselves (guides_*) opt out via BootstrapToolError.
    /// </summary>
    private const string GuideNudgeSuffix =
        " If this tool is unfamiliar, call `guides_read` with the tool name to fetch its full guide.";

    /// <summary>
    /// Executes a command that produces no typed result, returning a Result so the
    /// tool can branch on IsFailure and pass the failed Result to ToolError. Mirrors
    /// the typed overload's shape so every command call site reads the same way.
    /// </summary>
    protected async Task<Result> ExecuteCommandAsync<T>(Action<T>? configure = null) where T : IExecutableCommand
    {
        return await CommandService.ExecuteAsync(configure);
    }

    /// <summary>
    /// Executes a command that produces a typed result and returns it as a <c>Result&lt;TResult&gt;</c>.
    /// Tools should branch on IsFailure and pass the failed Result to ToolError so the
    /// agent sees the full message chain. On success, Value is guaranteed non-null.
    /// </summary>
    protected async Task<Result<TResult>> ExecuteCommandAsync<TCommand, TResult>(
        Action<TCommand>? configure = null)
        where TCommand : IExecutableCommand<TResult>
        where TResult : notnull
    {
        return await CommandService.ExecuteAsync<TCommand, TResult>(configure);
    }

    /// <summary>
    /// Creates a successful CallToolResult with a text message.
    /// </summary>
    protected static CallToolResult ToolSuccess(string text)
    {
        return new CallToolResult
        {
            Content = [
                new TextContentBlock
                {
                    Text = text
                }
            ]
        };
    }

    /// <summary>
    /// Creates an error CallToolResult with a text message. The generic
    /// guide-nudge suffix is appended so the agent knows it can fetch the
    /// tool's full guide via guides_read on a recoverable failure. Bootstrap
    /// tools that document themselves use BootstrapToolError instead.
    /// </summary>
    protected static CallToolResult ToolError(string text)
    {
        var withNudge = AppendGuideNudge(text);
        var capped = CapErrorMessage(withNudge);
        return new CallToolResult
        {
            IsError = true,
            Content = [
                new TextContentBlock
                {
                    Text = capped
                }
            ]
        };
    }

    /// <summary>
    /// Creates an error CallToolResult from a failed Result, surfacing its
    /// MessageChain so the agent sees the outer wrapper and any propagated
    /// inner causes. Adds the guide-nudge suffix.
    /// </summary>
    protected static CallToolResult ToolError(Result result)
    {
        return ToolError(result.MessageChain);
    }

    /// <summary>
    /// Bootstrap-tool error variant. Does not append the guide-nudge suffix —
    /// the bootstrap tools (guides_list, guides_read, guides_search) are how
    /// the agent reads guides in the first place, so nudging them at
    /// guides_read would be circular.
    /// </summary>
    protected static CallToolResult BootstrapToolError(string text)
    {
        var capped = CapErrorMessage(text);
        return new CallToolResult
        {
            IsError = true,
            Content = [
                new TextContentBlock
                {
                    Text = capped
                }
            ]
        };
    }

    private static string AppendGuideNudge(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return GuideNudgeSuffix.TrimStart();
        }

        // Trailing whitespace and punctuation already on the message stay as
        // written; the suffix has its own leading space.
        return text + GuideNudgeSuffix;
    }

    private static string CapErrorMessage(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= MaxErrorMessageLength)
        {
            return text;
        }

        const string ellipsis = "...";
        return string.Concat(text.AsSpan(0, MaxErrorMessageLength - ellipsis.Length), ellipsis);
    }

    /// <summary>
    /// Loads an embedded resource from the Celbridge.Tools assembly as a string.
    /// Returns a placeholder string when the resource is missing (build-time invariant).
    /// </summary>
    protected static string LoadEmbeddedResource(string resourceName)
    {
        var assembly = typeof(AgentToolBase).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return $"Resource '{resourceName}' not found.";
        }
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Creates a successful CallToolResult that carries both an image and a
    /// JSON metadata text block. The image is delivered as a typed MCP image
    /// content block so the multimodal client decodes it into the model's
    /// vision context directly. The text block lets the agent reference
    /// metadata (size, format, saved location, etc.) alongside the image.
    /// </summary>
    protected static CallToolResult ToolSuccessWithImage(byte[] imageBytes, string mimeType, string metadataJson)
    {
        return new CallToolResult
        {
            Content = [
                ImageContentBlock.FromBytes(imageBytes, mimeType),
                new TextContentBlock
                {
                    Text = metadataJson
                }
            ]
        };
    }
}
