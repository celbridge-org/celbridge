using Celbridge.BaseLibrary.Extensions;
using Celbridge.BaseLibrary.Scripting;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.ScriptUtils;

public class Extension : IExtension
{
    public void ConfigureServices(IExtensionServiceCollection config)
    {
    }

    public Result Initialize()
    {
        // Todo: Register the Util Classes here with the scripting service
        // Todo: Scripting service loads the registered util classes and adds static usings for them.

        var scriptingService = ServiceLocator.ServiceProvider.GetRequiredService<IScriptingService>();

        scriptingService.AddContextSetupCommand("#r \"Celbridge.ScriptUtils\"");
        scriptingService.AddContextSetupCommand("using static Celbridge.ScriptUtils.CommonUtils;");

        return Result.Ok();
    }
}
