using Celbridge.Console;
using Celbridge.Extensions;
using Celbridge.Console.Services;
using Celbridge.Console.ViewModels;
using Celbridge.Console.Views;

namespace Celbridge.Console;

public class Extension : IExtension
{
    public void ConfigureServices(IExtensionServiceCollection config)
    {
        config.AddTransient<ConsolePanel>();
        config.AddTransient<ConsolePanelViewModel>();
        config.AddTransient<ConsoleView>();
        config.AddTransient<ConsoleViewModel>();
        config.AddSingleton<IConsoleService, ConsoleService>();
    }

    public Result Initialize()
    {
        return Result.Ok();
    }
}
