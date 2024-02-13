using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.Dependencies;
using Celbridge.CoreExtensions.Console;

namespace Celbridge.Console;

public class ConsoleExtension : IExtension
{
    public void ConfigureServices(IServiceConfiguration config)
    {
        config.AddSingleton<IConsoleService, ConsoleService>();
    }
}
