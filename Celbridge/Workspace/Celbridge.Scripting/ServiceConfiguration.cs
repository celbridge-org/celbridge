using Celbridge.Modules;
using Celbridge.Scripting.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Scripting;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //

        services.AddTransient<IScriptingService, ScriptingService>();
    }
}
