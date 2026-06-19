using Celbridge.Settings.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Settings;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //

        services.AddSingleton<ISettingsStore, ApplicationStore>();
        services.AddSingleton<ICredentialProtector, DpapiCredentialProtector>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IFeatureFlags, FeatureFlags>();
    }
}
