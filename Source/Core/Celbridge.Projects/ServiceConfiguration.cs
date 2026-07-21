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
        services.AddSingleton<IAppActivationService, AppActivationService>();
        services.AddSingleton<IProjectMigrationService, ProjectMigrationService>();
        services.AddSingleton<IProjectTemplateService, ProjectTemplateService>();
        services.AddSingleton<IMigrationStepRegistry, MigrationStepRegistry>();
        services.AddTransient<IProjectLoader, ProjectLoader>();
        services.AddSingleton<IProjectLoadReporter, ProjectLoadReporter>();
        services.AddTransient<ProjectUnloader>();
        services.AddTransient<ProjectFactory>();

        //
        // Register commands
        //

        services.AddTransient<ICreateProjectCommand, CreateProjectCommand>();
        services.AddTransient<ILoadProjectCommand, LoadProjectCommand>();
        services.AddTransient<IUnloadProjectCommand, UnloadProjectCommand>();
        services.AddTransient<IReloadProjectCommand, ReloadProjectCommand>();
        services.AddTransient<IWriteProjectConfigCommand, WriteProjectConfigCommand>();
    }
}

