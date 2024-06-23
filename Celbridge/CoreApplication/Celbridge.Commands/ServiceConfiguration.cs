using Celbridge.BaseLibrary.Commands.Project;
using Celbridge.BaseLibrary.Commands.Workspace;
using Celbridge.Commands.Project;
using Celbridge.Commands.Workspace;
using Celbridge.Services.Commands;

namespace Celbridge.Commands;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ICommandService, CommandService>();
        services.AddTransient<ICreateProjectCommand, CreateProjectCommand>();
        services.AddTransient<ILoadProjectCommand, LoadProjectCommand>();
        services.AddTransient<IUnloadProjectCommand, UnloadProjectCommand>();
        services.AddTransient<ISaveWorkspaceStateCommand, SaveWorkspaceStateCommand>();
    }

    public static void Initialize()
    {}
}
