using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tools;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IGuides, Guides>();
    }

    /// <summary>
    /// Initializes the Guides singleton at app startup. Loading and
    /// validation run here rather than on the first agent call, so any
    /// malformed embedded guide fails the app launch instead of failing a
    /// tool invocation later.
    /// </summary>
    public static void Initialize()
    {
        Guides.Initialize();
    }
}
