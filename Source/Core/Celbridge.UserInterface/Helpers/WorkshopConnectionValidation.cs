using Celbridge.Credentials;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Save-time validation rules for the Workshop connection settings.
/// </summary>
public static class WorkshopConnectionValidation
{
    /// <summary>
    /// Returns true when the URL is an absolute https address, or an http
    /// address on the loopback host (the localhost development exception).
    /// </summary>
    public static bool IsValidWorkshopUrl(string workshopUrl)
    {
        if (!Uri.TryCreate(workshopUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

        return uri.Scheme == Uri.UriSchemeHttp &&
               uri.IsLoopback;
    }

    /// <summary>
    /// Returns true when the key starts with the expected Application Key
    /// prefix. A typo guard for display, not a gate: keys without the prefix
    /// are still accepted.
    /// </summary>
    public static bool HasExpectedKeyPrefix(string applicationKey)
    {
        return applicationKey.StartsWith(CredentialConstants.ApplicationKeyPrefix, StringComparison.Ordinal);
    }
}
