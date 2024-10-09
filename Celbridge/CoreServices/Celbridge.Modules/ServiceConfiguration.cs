using Celbridge.Modules.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Modules;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //
        services.AddSingleton<IModuleService, ModuleService>();
    }

    public static void Initialize()
    {}
}
