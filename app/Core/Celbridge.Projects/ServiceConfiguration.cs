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
        services.AddSingleton<IProjectMigrationService, ProjectMigrationService>();
        services.AddSingleton<IProjectTemplateService, ProjectTemplateService>();
        services.AddTransient<IProjectConfigService, ProjectConfigService>();
        services.AddSingleton<MigrationStepRegistry>();
        services.AddTransient<ProjectLoader>();
        services.AddTransient<ProjectUnloader>();

        // New services for refactored project loading
        services.AddTransient<ProjectConfigReader>();
        services.AddTransient<ProjectFactory>();

        //
        // Register commands
        //

        services.AddTransient<ICreateProjectCommand, CreateProjectCommand>();
        services.AddTransient<ILoadProjectCommand, LoadProjectCommand>();
        services.AddTransient<IUnloadProjectCommand, UnloadProjectCommand>();
    }
}

