using Celbridge.Settings;
using Celbridge.Workspace;

namespace Celbridge.WebHost.Services;

public class WebViewService : IWebViewService
{
    private readonly IFeatureFlags _featureFlags;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public WebViewService(IFeatureFlags featureFlags, IWorkspaceWrapper workspaceWrapper)
    {
        _featureFlags = featureFlags;
        _workspaceWrapper = workspaceWrapper;
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

    public WebViewToolSupport GetWebViewToolSupport(ResourceKey resource)
    {
        // No workspace means no documents can be open and therefore no resource
        // can be supported by the webview_* tools.
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return NotSupported(
                "No project is loaded. Open a project before calling any webview_* tool.");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;

        var match = workspaceService.DocumentsService
            .GetOpenDocuments()
            .FirstOrDefault(document => document.FileResource == resource);
        if (match is null)
        {
            return NotSupported(
                $"Resource '{resource}' is not open in the editor. Open it with document_open before calling any webview_* tool.");
        }

        var contributingPackage = workspaceService.PackageService.GetContributingPackage(match.EditorId);
        if (contributingPackage is not null && contributingPackage.Info.DevToolsBlocked)
        {
            return NotSupported(
                $"Resource '{resource}' is open with the '{match.EditorId}' editor, but the contributing package '{contributingPackage.Info.Id}' has set DevToolsBlocked = true. The webview_* tools are not available for this editor by package policy.");
        }

        return Supported;
    }

    private static readonly WebViewToolSupport Supported = new(IsSupported: true, Reason: null);
    private static WebViewToolSupport NotSupported(string reason) => new(IsSupported: false, Reason: reason);
}
