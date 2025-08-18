using Celbridge.Console.Services;
using Celbridge.Console.ViewModels;
using Celbridge.Console.Views;

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
        // Register views
        //

        services.AddTransient<IConsolePanel, ConsolePanel>();

        //
        // Register view models
        //

        services.AddTransient<ConsolePanelViewModel>();

        //
        // Register commands
        // 

        services.AddTransient<IClearCommand, ClearCommand>();
        services.AddTransient<IClearHistoryCommand, ClearHistoryCommand>();
        services.AddTransient<IUndoCommand, UndoCommand>();
        services.AddTransient<IRedoCommand, RedoCommand>();
        services.AddTransient<IHelpCommand, HelpCommand>();
        services.AddTransient<IPrintCommand, PrintCommand>();
        services.AddTransient<IRunCommand, RunCommand>();
    }
}
