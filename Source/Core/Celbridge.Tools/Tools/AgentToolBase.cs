using Celbridge.Commands;

namespace Celbridge.Tools;

/// <summary>
/// Base class for MCP tool classes. Provides access to the main application's
/// services via IApplicationServiceProvider and convenience properties for
/// commonly used services.
/// </summary>
public abstract class AgentToolBase
{
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
}
