using Celbridge.BaseLibrary.Extensions;
using Celbridge.BaseLibrary.Scripting;

namespace Celbridge.Scripting;

public class Extension : IExtension
{
    public void ConfigureServices(IExtensionServiceCollection config)
    {
        config.AddTransient<IScriptingService, ScriptingService>();
    }

    public Result Initialize()
    {
        return Result.Ok();
    }
}
