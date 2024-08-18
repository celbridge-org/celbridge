using Celbridge.Projects.Commands;
using Celbridge.Projects.Services;

namespace Celbridge.Projects;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //
        services.AddSingleton<IProjectService, ProjectService>();

        //
        // Register commands
        //
        services.AddTransient<ICreateProjectCommand, CreateProjectCommand>();
        services.AddTransient<ILoadProjectCommand, LoadProjectCommand>();
        services.AddTransient<IUnloadProjectCommand, UnloadProjectCommand>();
    }
}