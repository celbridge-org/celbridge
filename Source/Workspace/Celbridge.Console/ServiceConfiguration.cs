using Celbridge.Console.Services;
using Celbridge.Packages;
using Celbridge.WebHost;

namespace Celbridge.Console;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //

        services.AddTransient<IConsoleService, ConsoleService>();
        services.AddTransient<ITerminal, Terminal>();

        //
        // Register console document editor
        //

        services.AddSingleton<IBundledPackageProvider, ConsoleBundledPackageProvider>();
        services.AddSingleton<IConsoleSessionProvider, ShellSessionProvider>();
        services.AddSingleton<ICustomEditorChannelProvider, ConsoleSessionChannelProvider>();
        services.AddTransient<IConsoleProcessOwner, ConsoleProcessOwner>();
        services.AddTransient<IConsoleSessionService, ConsoleSessionService>();

        //
        // Register commands
        //

        services.AddTransient<IRunCommand, RunCommand>();
    }
}
