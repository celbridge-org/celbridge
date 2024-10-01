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
        config.AddTransient<ICommandHistory, CommandHistory>();

        //
        // Register views
        //

        config.AddTransient<IConsolePanel, ConsolePanel>();

        //
        // Register view models
        //

        config.AddTransient<ConsolePanelViewModel>();

        //
        // Register commands
        // 

        config.AddTransient<IClearLogCommand, ClearLogCommand>();
        config.AddTransient<IClearHistoryCommand, ClearHistoryCommand>();
        config.AddTransient<IUndoCommand, UndoCommand>();
        config.AddTransient<IRedoCommand, RedoCommand>();
        config.AddTransient<IHelpCommand, HelpCommand>();
    }
}
