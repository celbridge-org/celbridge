using Celbridge.Commands;

namespace Celbridge.MCPTools.Tools;

/// <summary>
/// Base class for MCP tool classes. Provides access to the main application's
/// services via IApplicationServiceProvider and convenience properties for
/// commonly used services.
/// </summary>
public abstract class ToolBase
{
    private readonly IApplicationServiceProvider _services;

    protected ToolBase(IApplicationServiceProvider services)
    {
        _services = services;
    }

    protected T GetRequiredService<T>() where T : class
    {
        return _services.GetRequiredService<T>();
    }

    protected ICommandService CommandService => GetRequiredService<ICommandService>();
}
