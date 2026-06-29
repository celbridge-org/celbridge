using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Celbridge.Server;
using Celbridge.WebHost;
using Celbridge.WebView.Services;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.WebView.ViewModels;

public partial class WebViewDocumentViewModel : DocumentViewModel
{

    private readonly ICommandService _commandService;
    private readonly IWebViewService _webViewService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IServerService _serverService;

    [ObservableProperty]
    private string _sourceUrl = string.Empty;

    /// <summary>
    /// Selects how LoadContent and NavigateUrl interpret the backing resource. Set
    /// by the view before the first LoadContent call. Defaults to ExternalUrl, which
    /// matches the .webview document behaviour assumed by the parameterless code-gen flow.
    /// </summary>
    public WebViewDocumentRole Role { get; set; }

    /// <summary>
    /// The URL the view should navigate to. For .webview documents this is the configured source URL
    /// verbatim; for the HTML viewer it is the loopback /project/ URL on the Skia heads, or the project
    /// virtual-host URL on Windows.
    /// </summary>
    public string NavigateUrl
    {
        get
        {
            if (Role == WebViewDocumentRole.HtmlViewer)
            {
                if (FileResource.IsEmpty)
                {
                    return string.Empty;
                }

                // URL path is the bare resource path; the "project:" prefix that
                // ResourceKey.ToString() now emits is for serialised diagnostics,
                // not URL construction.

                // Served over the loopback file server's /project/ route. Relative asset
                // references in the HTML resolve against this origin.
                return $"http://127.0.0.1:{_serverService.Port}/project/{FileResource.Path}";
            }

            return SourceUrl;
        }
    }

    // Code gen requires a parameterless constructor
    public WebViewDocumentViewModel()
    {
        throw new NotImplementedException();
    }

    public WebViewDocumentViewModel(
        ICommandService commandService,
        IWebViewService webViewService,
        IWorkspaceWrapper workspaceWrapper,
        IServerService serverService)
    {
        _commandService = commandService;
        _webViewService = webViewService;
        _workspaceWrapper = workspaceWrapper;
        _serverService = serverService;
    }

    public async Task<Result> LoadContent()
    {
        if (Role == WebViewDocumentRole.HtmlViewer)
        {
            // HTML viewer content is served by the file server (loopback /project/ route, or the project
            // virtual host on Windows). Nothing to parse; succeeding here lets TryNavigate run.
            await Task.CompletedTask;
            return Result.Ok();
        }

        // The .webview file is a small JSON document that carries the configured
        // external URL. Read via the gateway so the load picks up the same
        // containment validation as writes.
        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        var infoResult = await resourceFileSystem.GetInfoAsync(FileResource);
        if (infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.NotFound)
        {
            // No file on disk yet (e.g. just created via the Add File dialog).
            // Treat as a blank URL so the view shows nothing rather than failing.
            SourceUrl = string.Empty;
            return Result.Ok();
        }

        var readResult = await resourceFileSystem.ReadAllTextAsync(FileResource);
        if (readResult.IsFailure)
        {
            return Result.Fail($"Failed to read '{ExplorerConstants.WebViewExtension}' file '{FileResource}'")
                .WithErrors(readResult);
        }

        var parseResult = WebViewFileContent.TryParse(readResult.Value);
        if (parseResult.IsFailure)
        {
            return Result.Fail($"Failed to parse '{ExplorerConstants.WebViewExtension}' file '{FileResource}'")
                .WithErrors(parseResult);
        }

        var sourceUrl = parseResult.Value.SourceUrl.Trim();
        if (string.IsNullOrEmpty(sourceUrl))
        {
            SourceUrl = string.Empty;
            return Result.Ok();
        }

        if (!_webViewService.IsExternalUrl(sourceUrl))
        {
            return Result.Fail(
                $"{ExplorerConstants.WebViewExtension} documents only support external http/https URLs. Configured URL: '{sourceUrl}'");
        }

        SourceUrl = sourceUrl;
        return Result.Ok();
    }

    public void OpenBrowser(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        _commandService.Execute<IOpenBrowserCommand>(command =>
        {
            command.URL = url;
        });
    }
}
