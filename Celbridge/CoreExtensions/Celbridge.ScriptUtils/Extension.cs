using Celbridge.BaseLibrary.Extensions;
using Celbridge.BaseLibrary.Scripting;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.ScriptUtils;

public class Extension : IExtension
{
    public void ConfigureServices(IExtensionServiceCollection config)
    {}

    public Result Initialize()
    {
        var scriptingService = ServiceLocator.ServiceProvider.GetRequiredService<IScriptingService>();

        scriptingService.AddContextSetupCommand("#r \"Celbridge.ScriptUtils\"");
        scriptingService.AddContextSetupCommand("using static Celbridge.ScriptUtils.Services.CommonUtils;");

        return Result.Ok();
    }
}
