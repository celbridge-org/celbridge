using System.Text.Json;
using Celbridge.Server;
using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.WebView;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.WebApp.ViewModels;

public partial class WebAppDocumentViewModel : DocumentViewModel
{
    private readonly ICommandService _commandService;
    private readonly IFileServer _fileServer;
    private readonly IWebViewService _webViewService;
    private readonly ILogger<WebAppDocumentViewModel> _logger;

    [ObservableProperty]
    private string _sourceUrl = string.Empty;

    /// <summary>
    /// Whether the source URL requires the local file server to resolve.
    /// </summary>
    public bool NeedsFileServer => _webViewService.NeedsFileServer(SourceUrl);

    /// <summary>
    /// The resolved URL to navigate to. Resolved lazily from SourceUrl
    /// each time it is accessed, so that the project file server has
    /// time to be re-enabled after a workspace reload.
    /// </summary>
    public string NavigateUrl => ResolveNavigateUrl(SourceUrl);

    // Code gen requires a parameterless constructor
    public WebAppDocumentViewModel()
    {
        throw new NotImplementedException();
    }

    public WebAppDocumentViewModel(
        ICommandService commandService,
        IFileServer projectFileServer,
        IWebViewService webViewService,
        ILogger<WebAppDocumentViewModel> logger)
    {
        _commandService = commandService;
        _fileServer = projectFileServer;
        _webViewService = webViewService;
        _logger = logger;
    }

    public async Task<Result> LoadContent()
    {
        try
        {
            var text = await File.ReadAllTextAsync(FilePath);

            if (string.IsNullOrEmpty(text))
            {
                SourceUrl = string.Empty;
                return Result.Ok();
            }

            using var document = JsonDocument.Parse(text);
            if (document.RootElement.TryGetProperty("sourceUrl", out var urlElement))
            {
                SourceUrl = urlElement.GetString()?.Trim() ?? string.Empty;
                return Result.Ok();
            }
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when loading document from file: {FilePath}")
                .WithException(ex);
        }

        return Result.Fail($"Failed to load content from .webapp file: {FileResource}");
    }

    private string ResolveNavigateUrl(string sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            return string.Empty;
        }

        var trimmedUrl = sourceUrl.Trim();
        var urlKind = _webViewService.ClassifyUrl(trimmedUrl);

        switch (urlKind)
        {
            case UrlType.WebUrl:
                return trimmedUrl;

            case UrlType.LocalAbsolute:
                var resourcePath = _webViewService.StripLocalScheme(trimmedUrl);
                return _fileServer.ResolveLocalFileUrl(resourcePath, FileResource);

            case UrlType.LocalPath:
                return _fileServer.ResolveLocalFileUrl(trimmedUrl, FileResource);

            default:
                return string.Empty;
        }
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
