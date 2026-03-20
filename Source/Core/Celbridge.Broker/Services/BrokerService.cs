using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Celbridge.Broker.Services;

/// <summary>
/// Implements the broker service by wiring together tool discovery
/// and tool execution. Discovers tools from loaded assemblies at
/// initialization and routes CallToolAsync requests through the executor.
/// </summary>
public class BrokerService : IBrokerService
{
    private readonly ILogger<BrokerService> _logger;
    private readonly ToolRegistry _toolRegistry;
    private readonly ToolExecutor _toolExecutor;

    public BrokerService(
        ILogger<BrokerService> logger,
        ToolRegistry toolRegistry,
        ToolExecutor toolExecutor)
    {
        _logger = logger;
        _toolRegistry = toolRegistry;
        _toolExecutor = toolExecutor;
    }

    /// <summary>
    /// Discovers tools from the given assemblies. Call this during application startup.
    /// </summary>
    public void Initialize(IEnumerable<Assembly> assemblies)
    {
        _toolRegistry.DiscoverTools(assemblies);
    }

    /// <summary>
    /// Returns all discovered tool descriptors.
    /// </summary>
    public IReadOnlyList<ToolDescriptor> GetTools()
    {
        return _toolRegistry.GetTools();
    }

    /// <summary>
    /// Invokes a tool by its slash-separated name, passing the given arguments.
    /// </summary>
    public async Task<ToolCallResult> CallToolAsync(
        string toolName,
        IDictionary<string, object?> arguments)
    {
        var descriptor = _toolRegistry.FindTool(toolName);
        if (descriptor is null)
        {
            _logger.LogWarning("Unknown tool '{ToolName}'", toolName);
            return ToolCallResult.Failure($"Unknown tool '{toolName}'");
        }

        _logger.LogDebug("Calling tool '{ToolName}'", toolName);
        return await _toolExecutor.ExecuteAsync(descriptor, arguments);
    }
}
