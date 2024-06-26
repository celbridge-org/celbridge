using Celbridge.BaseLibrary.Project;
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
    }
}