using Celbridge.Scripting;

namespace Celbridge.ScriptUtils;

public static class ServiceConfiguration
{
    public static Result Initialize()
    {
        //
        // Bind to the utilities defined in this assembly
        //
        var scriptingService = ServiceLocator.ServiceProvider.GetRequiredService<IScriptingService>();
        scriptingService.AddContextSetupCommand("#r \"Celbridge.ScriptUtils\"");
        scriptingService.AddContextSetupCommand("using static Celbridge.ScriptUtils.Services.CommonUtils;");

        return Result.Ok();
    }
}
