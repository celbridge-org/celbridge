using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Utilities;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IEnvironmentService, EnvironmentService>();
        services.AddTransient<IDumpFile, DumpFile>();
    }
}
