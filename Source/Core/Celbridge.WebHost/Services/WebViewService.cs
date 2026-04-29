using Celbridge.Settings;

namespace Celbridge.WebHost.Services;

public class WebViewService : IWebViewService
{
    private readonly IFeatureFlags _featureFlags;

    public WebViewService(IFeatureFlags featureFlags)
    {
        _featureFlags = featureFlags;
    }

    public bool IsExternalUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var trimmed = url.Trim();

        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsDevToolsFeatureEnabled()
    {
        return _featureFlags.IsEnabled(FeatureFlagConstants.WebViewDevTools);
    }

    public bool IsDevToolsEvalFeatureEnabled()
    {
        return _featureFlags.IsEnabled(FeatureFlagConstants.WebViewDevToolsEval);
    }
}
