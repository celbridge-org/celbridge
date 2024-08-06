using Celbridge.Resources;
using Celbridge.ProjectAdmin.Commands;
using Celbridge.ProjectAdmin.Services;

namespace Celbridge.ProjectAdmin;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //
        services.AddSingleton<IProjectDataService, ProjectDataService>();

        //
        // Register commands
        //
        services.AddTransient<ICreateProjectCommand, CreateProjectCommand>();
        services.AddTransient<ILoadProjectCommand, LoadProjectCommand>();
        services.AddTransient<IUnloadProjectCommand, UnloadProjectCommand>();
    }
}