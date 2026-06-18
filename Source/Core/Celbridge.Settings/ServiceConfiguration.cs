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

        if (IsStorageAPIAvailable)
        {
            services.AddTransient<IApplicationSettingsStore, LocalSettingsStore>();
        }
        else
        {
            services.AddTransient<IApplicationSettingsStore, InMemorySettingsStore>();
        }

        services.AddSingleton<ICredentialProtector, DpapiCredentialProtector>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IEditorSettings, EditorSettings>();
        services.AddSingleton<IFeatureFlags, FeatureFlags>();
    }

    private static bool IsStorageAPIAvailable
    {
        get
        {
#if WINDOWS
            try
            {
                var package = Windows.ApplicationModel.Package.Current;
                return package is not null;
            }
            catch (InvalidOperationException)
            {
                // Exception thrown if the app is unpackaged
                return false;
            }
#else
            return true;
#endif
        }

    }
}
