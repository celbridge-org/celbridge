using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Celbridge.WebHost;
using Celbridge.WebView.Services;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.WebView.ViewModels;

public partial class WebViewDocumentViewModel : DocumentViewModel
{
    private const string ProjectVirtualHost = "project.celbridge";
    private const string SourceUrlFieldName = "source_url";

    private readonly ICommandService _commandService;
    private readonly IWebViewService _webViewService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    [ObservableProperty]
    private string _sourceUrl = string.Empty;

    /// <summary>
    /// Selects how LoadContent and NavigateUrl interpret the backing resource. Set
    /// by the view before the first LoadContent call. Defaults to ExternalUrl, which
    /// matches the .webview.cel document behaviour assumed by the parameterless code-gen flow.
    /// </summary>
    public WebViewDocumentRole Role { get; set; }

    /// <summary>
    /// The URL the view should navigate to. For .webview.cel documents this is the configured
    /// source URL verbatim; for the HTML viewer it is the project virtual-host URL derived
    /// from FileResource.
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
                return $"https://{ProjectVirtualHost}/{FileResource.Path}";
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
        IWorkspaceWrapper workspaceWrapper)
    {
        _commandService = commandService;
        _webViewService = webViewService;
        _workspaceWrapper = workspaceWrapper;
    }

    public async Task<Result> LoadContent()
    {
        if (Role == WebViewDocumentRole.HtmlViewer)
        {
            // HTML viewer reads the file directly via the project virtual host.
            // Nothing to parse; succeeding here is enough to allow TryNavigate to run.
            await Task.CompletedTask;
            return Result.Ok();
        }

        // The .webview.cel file is a standalone .cel form: SidecarService.ReadAsync
        // treats the resource itself as the storage, parses the TOML frontmatter
        // through SidecarHelper, and routes IO via the gateway so this read
        // coordinates with concurrent writes from the inspector panel.
        var sidecarService = _workspaceWrapper.WorkspaceService.ResourceService.Sidecars;
        var readResult = await sidecarService.ReadAsync(FileResource);
        if (readResult.IsFailure)
        {
            return Result.Fail($"Failed to read '.webview.cel' file '{FileResource}'")
                .WithErrors(readResult);
        }
        var read = readResult.Value;

        if (read.Outcome == SidecarReadOutcome.Broken)
        {
            return Result.Fail($"Failed to parse '.webview.cel' file '{FileResource}': {read.FailureMessage ?? "parse failed"}");
        }

        if (read.Outcome == SidecarReadOutcome.NoSidecar
            || read.Content is null
            || !read.Content.Frontmatter.TryGetValue(SourceUrlFieldName, out var urlObject)
            || urlObject is not string urlValue)
        {
            // No file, no frontmatter, or no source_url. Treat as a blank URL so
            // the view shows nothing rather than failing the open.
            SourceUrl = string.Empty;
            return Result.Ok();
        }

        var sourceUrl = urlValue.Trim();
        if (string.IsNullOrEmpty(sourceUrl))
        {
            SourceUrl = string.Empty;
            return Result.Ok();
        }

        if (!_webViewService.IsExternalUrl(sourceUrl))
        {
            return Result.Fail($".webview.cel documents only support external http/https URLs. Configured URL: '{sourceUrl}'");
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
