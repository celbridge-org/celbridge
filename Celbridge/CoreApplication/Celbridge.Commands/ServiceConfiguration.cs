using Celbridge.BaseLibrary.Commands.Project;
using Celbridge.BaseLibrary.Commands.Workspace;
using Celbridge.Commands.Services;
using Celbridge.Commands.Workspace;

namespace Celbridge.Commands;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ICommandService, CommandService>();
        services.AddTransient<ISaveWorkspaceStateCommand, SaveWorkspaceStateCommand>();
    }
}
