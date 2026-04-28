using System.Text.Json;
using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.WebHost;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.WebView.ViewModels;

public partial class WebViewDocumentViewModel : DocumentViewModel
{
    private readonly ICommandService _commandService;
    private readonly IWebViewService _webViewService;
    private readonly ILogger<WebViewDocumentViewModel> _logger;

    [ObservableProperty]
    private string _sourceUrl = string.Empty;

    /// <summary>
    /// The URL to navigate to. .webview documents host external URLs only,
    /// so this is the configured source URL verbatim.
    /// </summary>
    public string NavigateUrl => SourceUrl;

    // Code gen requires a parameterless constructor
    public WebViewDocumentViewModel()
    {
        throw new NotImplementedException();
    }

    public WebViewDocumentViewModel(
        ICommandService commandService,
        IWebViewService webViewService,
        ILogger<WebViewDocumentViewModel> logger)
    {
        _commandService = commandService;
        _webViewService = webViewService;
        _logger = logger;
    }

    public async Task<Result> LoadContent()
    {
        string sourceUrl;
        try
        {
            var text = await File.ReadAllTextAsync(FilePath);

            if (string.IsNullOrEmpty(text))
            {
                SourceUrl = string.Empty;
                return Result.Ok();
            }

            using var document = JsonDocument.Parse(text);
            if (!document.RootElement.TryGetProperty("sourceUrl", out var urlElement))
            {
                return Result.Fail($"Failed to load content from .webview file: {FileResource}");
            }

            sourceUrl = urlElement.GetString()?.Trim() ?? string.Empty;
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when loading document from file: {FilePath}")
                .WithException(ex);
        }

        if (string.IsNullOrEmpty(sourceUrl))
        {
            SourceUrl = string.Empty;
            return Result.Ok();
        }

        var urlKind = _webViewService.ClassifyUrl(sourceUrl);
        if (urlKind != UrlType.WebUrl)
        {
            return Result.Fail($".webview documents only support external http/https URLs. Configured URL: '{sourceUrl}'");
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
