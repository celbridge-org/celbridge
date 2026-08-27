namespace Celbridge.WebView.Helpers;

/// <summary>
/// Turns an address the user typed into one the embedded browser can navigate to, and decides whether two
/// addresses refer to the same page.
/// </summary>
public static class WebViewUrlHelper
{
    /// <summary>
    /// Validates an address, prefixing https:// when no scheme was typed. Returns false when the input
    /// cannot become a navigable external URL. A loopback address is a normal destination, so a local
    /// development server normalizes like any other host.
    /// </summary>
    public static bool TryNormalize(string input, out string url)
    {
        url = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = $"https://{trimmed}";
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp
            && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        url = trimmed;

        return true;
    }

    /// <summary>
    /// Whether two addresses refer to the same page. Each is reduced to its absolute form first, so a
    /// missing trailing slash or an unlisted default port does not read as a different page. False when
    /// either address cannot be navigated to, there being no page for it to be the same as.
    /// </summary>
    public static bool IsSameUrl(string url, string otherUrl)
    {
        if (!TryGetAbsoluteUrl(url, out var absoluteUrl)
            || !TryGetAbsoluteUrl(otherUrl, out var otherAbsoluteUrl))
        {
            return false;
        }

        return absoluteUrl == otherAbsoluteUrl;
    }

    // The absolute form of an address, which is the form two of them can be compared in.
    private static bool TryGetAbsoluteUrl(string input, out string absoluteUrl)
    {
        absoluteUrl = string.Empty;

        if (!TryNormalize(input, out var normalized))
        {
            return false;
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            return false;
        }

        absoluteUrl = uri.AbsoluteUri;

        return true;
    }
}
