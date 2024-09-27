using Celbridge.Console.Services;
using Celbridge.Console.ViewModels;
using Celbridge.Console.Views;
using Celbridge.Extensions;

namespace Celbridge.Console;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IExtensionServiceCollection config)
    {
        //
        // Register services
        //

        config.AddTransient<IConsoleService, ConsoleService>();

        //
        // Register views
        //

        config.AddTransient<IConsolePanel, ConsolePanel>();
        config.AddTransient<ConsoleView>();

        //
        // Register view models
        //

        config.AddTransient<ConsolePanelViewModel>();
        config.AddTransient<ConsoleViewModel>();
    }
}
