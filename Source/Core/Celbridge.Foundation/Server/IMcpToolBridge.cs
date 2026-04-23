namespace Celbridge.Server;

/// <summary>
/// Describes a single tool parameter as enumerated from the MCP server.
/// </summary>
public record ToolParameter(
    string Name,
    string Type,
    string Description,
    bool HasDefaultValue,
    object? DefaultValue,
    string? ItemType = null);

/// <summary>
/// Describes a tool exposed by the MCP server.
/// </summary>
public record ToolDescriptor(
    string Name,
    string Alias,
    string Description,
    string ReturnType,
    IReadOnlyList<ToolParameter> Parameters);

/// <summary>
/// Outcome of invoking an MCP tool.
/// </summary>
public record ToolCallResult(bool IsSuccess, string ErrorMessage, object? Value);

/// <summary>
/// Typed gateway onto the MCP tool registry, used by consumers that enumerate
/// or invoke tools from C# (contribution editors, automation, etc.).
/// </summary>
public interface IMcpToolBridge
{
    /// <summary>
    /// Returns all tools currently registered with the MCP server.
    /// </summary>
    Task<IReadOnlyList<ToolDescriptor>> ListToolsAsync();

    /// <summary>
    /// Invokes a named tool and returns its result. Arguments may be any
    /// JSON-shaped value (JsonElement, JObject, or a plain CLR dictionary);
    /// implementations normalize before sending to the MCP transport.
    /// </summary>
    Task<ToolCallResult> CallToolAsync(string name, object? arguments);
}
