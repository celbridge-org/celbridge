using Celbridge.ProjectSettings.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.ProjectSettings;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register view models
        //

        services.AddTransient<ProjectSettingsEditorViewModel>();
    }
}
