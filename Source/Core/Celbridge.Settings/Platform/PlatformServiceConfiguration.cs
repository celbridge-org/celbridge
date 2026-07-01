using Celbridge.Settings.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Settings.Platform;

/// <summary>
/// Registers the Settings services whose implementation is selected per platform.
/// </summary>
public static class PlatformServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        // The credential store is platform-specific: macOS keeps the secret in the login Keychain, other
        // heads encrypt it with DPAPI and persist the ciphertext in the application settings store.
        if (OperatingSystem.IsMacOS())
        {
            services.AddSingleton<ICredentialStore, MacOSKeychainCredentialStore>();
        }
        else
        {
            services.AddSingleton<ICredentialStore, DpapiCredentialStore>();
        }
    }
}
