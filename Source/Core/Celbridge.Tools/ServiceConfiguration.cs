using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tools;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IDocLibrary, DocLibrary>();
    }

    /// <summary>
    /// Initializes the DocLibrary singleton at app startup. Loading and
    /// validation run here rather than on the first agent call, so any
    /// malformed embedded doc fails the app launch instead of failing a tool
    /// invocation later.
    /// </summary>
    public static void Initialize()
    {
        DocLibrary.Initialize();
    }
}
