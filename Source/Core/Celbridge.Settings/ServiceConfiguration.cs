using Celbridge.Credentials;
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

        services.AddSingleton<IEditorSettings, EditorSettings>();
        services.AddSingleton<IFeatureFlags, FeatureFlags>();
        services.AddSingleton<ICredentialProtector, DpapiCredentialProtector>();
        services.AddSingleton<ICredentialService, CredentialService>();

        if (IsStorageAPIAvailable)
        {
            services.AddTransient<ISettingsGroup, SettingsGroup>();
        }
        else
        {
            services.AddTransient<ISettingsGroup, TempSettingsGroup>();
        }
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
