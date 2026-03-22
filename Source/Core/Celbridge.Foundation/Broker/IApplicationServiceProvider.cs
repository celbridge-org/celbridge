namespace Celbridge.Broker;

/// <summary>
/// Provides access to the main application's DI container from contexts
/// that have their own separate service provider (e.g. the MCP HTTP server).
/// Tool classes use this to resolve application services like ICommandService.
/// </summary>
public interface IApplicationServiceProvider
{
    /// <summary>
    /// Gets a required service from the main application's DI container.
    /// </summary>
    T GetRequiredService<T>() where T : class;
}
