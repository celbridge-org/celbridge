namespace Celbridge.WebView.Services;

public class WebViewService : IWebViewService
{
    private const string LocalSchemePrefix = "local://";

    public UrlType ClassifyUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return UrlType.Invalid;
        }

        var trimmed = url.Trim();

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return UrlType.WebUrl;
        }

        if (trimmed.StartsWith(LocalSchemePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var resourcePath = trimmed.Substring(LocalSchemePrefix.Length);

            // local:// must specify an absolute resource key, not a relative path
            if (string.IsNullOrEmpty(resourcePath) ||
                resourcePath.Contains("..") ||
                resourcePath.StartsWith("/") ||
                resourcePath.StartsWith("\\"))
            {
                return UrlType.Invalid;
            }

            return UrlType.LocalAbsolute;
        }

        if (trimmed.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
        {
            return UrlType.LocalPath;
        }

        return UrlType.Invalid;
    }

    public bool NeedsFileServer(string url)
    {
        var kind = ClassifyUrl(url);
        return kind == UrlType.LocalAbsolute || kind == UrlType.LocalPath;
    }

    public string StripLocalScheme(string url)
    {
        if (url.StartsWith(LocalSchemePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return url.Substring(LocalSchemePrefix.Length);
        }

        return url;
    }
}
