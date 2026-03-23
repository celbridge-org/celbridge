using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Server.Services;

/// <summary>
/// Wraps the main application's IServiceProvider so that MCP tool classes
/// can resolve application services without needing per-service forwarding.
/// </summary>
public class ApplicationServiceProvider : IApplicationServiceProvider
{
    private readonly IServiceProvider _serviceProvider;

    public ApplicationServiceProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public T GetRequiredService<T>() where T : class
    {
        return _serviceProvider.GetRequiredService<T>();
    }
}
