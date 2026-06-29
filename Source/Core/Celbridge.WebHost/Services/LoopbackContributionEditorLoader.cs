using Celbridge.Packages;
using Celbridge.Server;

namespace Celbridge.WebHost.Services;

// Default contribution editor loader: serves the editor from the loopback file server over the WebSocket
// host channel. Matches every package and is resolved as the fallback, so it hosts any editor a custom
// loader does not claim.
internal sealed class LoopbackContributionEditorLoader : IContributionEditorLoader
{
    private readonly IFileServer _fileServer;

    public LoopbackContributionEditorLoader(IFileServer fileServer)
    {
        _fileServer = fileServer;
    }

    public bool CanLoad(PackageInfo package) => true;

    public HostChannelTransport GetTransport(PackageInfo package) => HostChannelTransport.LoopbackWebSocket;

    public string GetAllowedNavigationOrigin(ContributionEditorLoadRequest request)
    {
        return _fileServer.GetPackageUrl(request.PackageUrlName, string.Empty);
    }

    public Task LoadAsync(ContributionEditorLoadRequest request)
    {
        var entryUrl = _fileServer.GetPackageUrl(request.PackageUrlName, request.EntryPoint);
        var navigationUrl = HostChannelFactory.AppendConnectionToken(entryUrl, request.ConnectionToken);
        request.CoreWebView2.Navigate(navigationUrl);

        return Task.CompletedTask;
    }
}
