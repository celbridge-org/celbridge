using Celbridge.Extensions;
using Celbridge.Scripting.Services;

namespace Celbridge.Scripting;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IExtensionServiceCollection config)
    {
        //
        // Register services
        //
        config.AddSingleton<IScriptingService, ScriptingService>();
    }
}
