using Celbridge.Utilities;
using Celbridge.Commands.Services;

namespace Celbridge.Commands;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ICommandService, CommandService>();

        services.AddTransient<IExecutedCommandLogger, ExecutedCommandLogger>();
    }
}
