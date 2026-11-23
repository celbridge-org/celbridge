using Celbridge.Settings;

namespace Celbridge.Console.Helpers;

/// <summary>
/// Decides whether a console of a session type can start in the loaded project.
/// </summary>
public static class ConsoleSessionFeature
{
    /// <summary>
    /// Returns true when the loaded project enables consoles, and enables the provider's feature when the provider
    /// names one.
    /// </summary>
    public static bool IsEnabled(IConsoleSessionProvider provider, IFeatureFlags featureFlags)
    {
        if (!featureFlags.IsEnabled(FeatureFlagCatalog.Consoles))
        {
            return false;
        }

        return provider.Feature is null
            || featureFlags.IsEnabled(provider.Feature);
    }
}
