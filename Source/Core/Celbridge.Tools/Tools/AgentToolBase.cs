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

    protected AgentToolBase(IApplicationServiceProvider services)
    {
        _services = services;
    }

    protected T GetRequiredService<T>() where T : class
    {
        return _services.GetRequiredService<T>();
    }

    protected ICommandService CommandService => GetRequiredService<ICommandService>();

    /// <summary>
    /// Executes a command asynchronously, returning a CallToolResult so the MCP
    /// framework can report errors to the client.
    /// </summary>
    protected async Task<CallToolResult> ExecuteCommandAsync<T>(Action<T>? configure = null) where T : IExecutableCommand
    {
        var result = await CommandService.ExecuteAsync(configure);
        if (result.IsFailure)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = result.FirstErrorMessage }]
            };
        }

        return SuccessResult("ok");
    }

    /// <summary>
    /// Executes a command that produces a typed result, returning both a CallToolResult
    /// and the result value for the MCP tool to include in its response.
    /// </summary>
    protected async Task<(CallToolResult, TResult?)> ExecuteCommandAsync<TCommand, TResult>(
        Action<TCommand>? configure = null)
        where TCommand : IExecutableCommand<TResult>
        where TResult : notnull
    {
        var result = await CommandService.ExecuteAsync<TCommand, TResult>(configure);
        if (result.IsFailure)
        {
            var errorResult = new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = result.FirstErrorMessage }]
            };
            return (errorResult, default);
        }

        return (new CallToolResult(), result.Value);
    }

    /// <summary>
    /// Creates a successful CallToolResult with a text message.
    /// </summary>
    protected static CallToolResult SuccessResult(string text)
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
    /// Creates an error CallToolResult with a text message.
    /// </summary>
    protected static CallToolResult ErrorResult(string text)
    {
        return new CallToolResult
        {
            IsError = true,
            Content = [
                new TextContentBlock
                {
                    Text = text
                }
            ]
        };
    }

    /// <summary>
    /// Creates a successful CallToolResult that carries both an image and a
    /// JSON metadata text block. The image is delivered as a typed MCP image
    /// content block so the multimodal client decodes it into the model's
    /// vision context directly. The text block lets the agent reference
    /// metadata (size, format, saved location, etc.) alongside the image.
    /// </summary>
    protected static CallToolResult SuccessResultWithImage(byte[] imageBytes, string mimeType, string metadataJson)
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
