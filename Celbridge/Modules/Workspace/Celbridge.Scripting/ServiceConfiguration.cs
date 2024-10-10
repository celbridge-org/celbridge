using Celbridge.Modules;
using Celbridge.Scripting.Services;

namespace Celbridge.Scripting;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IModuleServiceCollection config)
    {
        //
        // Register services
        //

        config.AddTransient<IScriptingService, ScriptingService>();
    }
}
