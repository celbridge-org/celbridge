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
        if (OperatingSystem.IsMacOS())
        {
            services.AddSingleton<ICredentialStore, MacOSKeychainCredentialStore>();
        }
        else
        {
            services.AddSingleton<ICredentialStore, DpapiCredentialStore>();
        }
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IFeatureFlags, FeatureFlags>();
    }
}
