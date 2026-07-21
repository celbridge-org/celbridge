using Celbridge.ProjectSettings.ViewModels;
using Celbridge.ProjectSettings.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.ProjectSettings;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register views
        //

        services.AddTransient<IProjectSettingsPanel, ProjectSettingsPanel>();

        //
        // Register view models
        //

        services.AddTransient<ProjectSettingsPanelViewModel>();
    }
}
