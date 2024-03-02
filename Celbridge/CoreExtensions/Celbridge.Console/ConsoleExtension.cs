using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.Extensions;

namespace Celbridge.Console;

public class ConsoleExtension : IExtension
{
    public void ConfigureServices(IExtensionServiceCollection config)
    {
        config.AddSingleton<IConsoleService, ConsoleService>();
    }

    public Result Initialize()
    {
        return Result.Ok();
    }
}
