using Celbridge.BaseLibrary.Commands;
using Celbridge.Commands.Commands;
using Celbridge.Commands.Services;

namespace Celbridge.Commands;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //

        services.AddSingleton<ICommandService, CommandService>();

        //
        // Register commands
        //

        services.AddTransient<IUndoCommand, UndoCommand>();
        services.AddTransient<IRedoCommand, RedoCommand>();
    }
}
