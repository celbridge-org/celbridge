using System.Reflection;

namespace Celbridge.Broker;

/// <summary>
/// The broker provides a unified interface for tool discovery and execution.
/// All external clients (Python, JavaScript, AI agents) interact with the
/// application through this service.
/// </summary>
public interface IBrokerService
{
    /// <summary>
    /// Discovers tools from the given assemblies. Call this during application startup.
    /// </summary>
    void Initialize(IEnumerable<Assembly> assemblies);

    /// <summary>
    /// Returns all discovered tool descriptors.
    /// </summary>
    IReadOnlyList<ToolDescriptor> GetTools();

    /// <summary>
    /// Invokes a tool by its slash-separated name, passing the given arguments.
    /// </summary>
    Task<ToolCallResult> CallToolAsync(string toolName, IDictionary<string, object?> arguments);
}
