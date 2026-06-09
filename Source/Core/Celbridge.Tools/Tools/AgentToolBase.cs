using System.Text.Json;
using System.Text.Json.Serialization;
using Celbridge.Commands;
using ModelContextProtocol.Protocol;

namespace Celbridge.Tools;

/// <summary>
/// Base class for MCP tool classes. Provides access to the main application's
/// services via IApplicationServiceProvider and convenience properties for
/// commonly used services. Response shaping (success/error CallToolResult
/// construction, guide pointers, error capping) lives on ToolResponse.
/// </summary>
public abstract class AgentToolBase
{
    // UnmappedMemberHandling.Disallow makes typed deserialisation reject unknown
    // fields. Agents that typo a property name (e.g. lowColor vs minColor on a
    // conditional formatting rule) get a clear error instead of silently running
    // with defaults for the field they meant to set.
    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
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
    /// Executes a command that produces no typed result, returning a Result so the
    /// tool can branch on IsFailure and pass the failed Result to ToolResponse.Error.
    /// Mirrors the typed overload's shape so every command call site reads the same way.
    /// </summary>
    protected async Task<Result> ExecuteCommandAsync<T>(Action<T>? configure = null) where T : IExecutableCommand
    {
        return await CommandService.ExecuteAsync(configure);
    }

    /// <summary>
    /// Executes a command that produces a typed result and returns it as a typed Result.
    /// Tools should branch on IsFailure and pass the failed Result to ToolResponse.Error so the
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
    /// Parses a JSON tool argument into T. Failures return "Invalid {label}: ..."
    /// — unmapped-property errors list the valid names on T (or its element
    /// type for collections); other errors strip Celbridge.* type prefixes.
    /// </summary>
    protected static Result<T> ParseJsonArgument<T>(string json, string label) where T : class
        => JsonArgumentParser.Parse<T>(json, label, JsonOptions);

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
}
