namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Presentation helpers for the Workshop Key. The key is stored as an opaque
/// Protected setting; these helpers understand its "kpf_(prefix)_(secret)" format
/// to derive non-secret display data, which is a UI concern rather than a
/// storage one.
/// </summary>
public static class WorkshopKey
{
    private const string WorkshopKeyPrefix = "kpf_";

    /// <summary>
    /// Returns the identifying prefix of a Workshop Key shaped like
    /// "kpf_(prefix)_(secret)", or an empty string when the key does not match
    /// that shape, so that no secret material can leak into the hint.
    /// </summary>
    public static string GetDisplayHint(string workshopKey)
    {
        if (!workshopKey.StartsWith(WorkshopKeyPrefix, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var separatorIndex = workshopKey.IndexOf('_', WorkshopKeyPrefix.Length);
        if (separatorIndex < 0)
        {
            return string.Empty;
        }

        return workshopKey.Substring(0, separatorIndex);
    }
}
