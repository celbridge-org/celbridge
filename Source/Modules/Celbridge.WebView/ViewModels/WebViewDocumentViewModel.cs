using System.Text.Json;
using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Celbridge.WebHost;
using Celbridge.WebView.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.WebView.ViewModels;

public partial class WebViewDocumentViewModel : DocumentViewModel
{
    private const string ProjectVirtualHost = "project.celbridge";

    private readonly ICommandService _commandService;
    private readonly IWebViewService _webViewService;

    [ObservableProperty]
    private string _sourceUrl = string.Empty;

    /// <summary>
    /// Selects how LoadContent and NavigateUrl interpret the backing resource. Set
    /// by the view before the first LoadContent call. Defaults to ExternalUrl, which
    /// matches the .webview document behaviour assumed by the parameterless code-gen flow.
    /// </summary>
    public WebViewDocumentRole Role { get; set; }

    /// <summary>
    /// The URL the view should navigate to. For .webview documents this is the configured
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

                return $"https://{ProjectVirtualHost}/{FileResource}";
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
        IWebViewService webViewService)
    {
        _commandService = commandService;
        _webViewService = webViewService;
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

        if (!_webViewService.IsExternalUrl(sourceUrl))
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
