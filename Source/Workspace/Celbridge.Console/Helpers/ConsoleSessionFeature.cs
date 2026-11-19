using Celbridge.Settings;

namespace Celbridge.Console.Helpers;

/// <summary>
/// Decides whether a console of a session type can start in the loaded project.
/// </summary>
public static class ConsoleSessionFeature
{
    /// <summary>
    /// Returns true when the provider needs no feature, or the loaded project has its feature enabled.
    /// </summary>
    public static bool IsEnabled(IConsoleSessionProvider provider, IFeatureFlags featureFlags)
    {
        return provider.Feature is null
            || featureFlags.IsEnabled(provider.Feature);
    }
}
