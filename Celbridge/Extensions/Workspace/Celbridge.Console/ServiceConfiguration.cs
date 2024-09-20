using Celbridge.Console.Services;
using Celbridge.Console.ViewModels;
using Celbridge.Console.Views;
using Celbridge.Extensions;

namespace Celbridge.Console;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IExtensionServiceCollection config)
    {
        config.AddTransient<ConsolePanel>();
        config.AddTransient<ConsolePanelViewModel>();
        config.AddTransient<ConsoleView>();
        config.AddTransient<ConsoleViewModel>();
        config.AddTransient<IConsoleService, ConsoleService>();
    }
}
