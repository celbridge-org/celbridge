using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.Extensions;
using Celbridge.CoreExtensions.Console;

namespace Celbridge.Console;

public class ConsoleExtension : IExtension
{
    public void ConfigureServices(IExtensionServiceCollection config)
    {
        config.AddSingleton<IConsoleService, ConsoleService>();
    }
}
