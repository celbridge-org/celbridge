using Celbridge.Settings;

namespace Celbridge.Console.Helpers;

/// <summary>
/// Decides whether a console of a session type can start in the loaded project, and names the notice a console
/// shows when it cannot.
/// </summary>
public static class ConsoleSessionFeature
{
    private const string NoticeKeyPrefix = "Console_Disabled_";

    /// <summary>
    /// Returns true when the provider needs no feature, or the loaded project has its feature enabled.
    /// </summary>
    public static bool IsEnabled(IConsoleSessionProvider provider, IFeatureFlags featureFlags)
    {
        return provider.Feature is null
            || featureFlags.IsEnabled(provider.Feature);
    }

    /// <summary>
    /// Returns the resource key of the notice for a disabled session type. The key is the type id with its first
    /// letter capitalized, after a fixed prefix.
    /// </summary>
    public static string NoticeKey(string typeId)
    {
        return $"{NoticeKeyPrefix}{char.ToUpperInvariant(typeId[0])}{typeId[1..]}";
    }
}
