using System.Text.Json;
using Celbridge.Commands;
using Celbridge.Explorer;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class WebPageDocumentViewModel : DocumentViewModel
{
    private readonly ICommandService _commandService;

    [ObservableProperty]
    private string _sourceUrl = string.Empty;

    // Code gen requires a parameterless constructor
    public WebPageDocumentViewModel()
    {
        throw new NotImplementedException();
    }

    public WebPageDocumentViewModel(ICommandService commandService)
    {
        _commandService = commandService;
    }

    public async Task<Result> LoadContent()
    {
        try
        {
            var text = await File.ReadAllTextAsync(FilePath);

            if (string.IsNullOrEmpty(text))
            {
                // No data populated yet in the .web file, default to empty url
                SourceUrl = string.Empty;
                return Result.Ok();
            }

            using var document = JsonDocument.Parse(text);
            if (document.RootElement.TryGetProperty("sourceUrl", out var urlElement))
            {
                string targetUrl = urlElement.GetString()?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(targetUrl) &&
                    !targetUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                    !targetUrl.StartsWith("file", StringComparison.OrdinalIgnoreCase))
                {
                    targetUrl = $"https://{targetUrl}";
                }

                SourceUrl = targetUrl;
                return Result.Ok();
            }
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occured when loading document from file: {FilePath}")
                .WithException(ex);
        }

        return Result.Fail($"Failed to local content from .web file: {FileResource}");
    }

    public void OpenBrowser(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            // Navigating to an empty URL is a no-op
            return;
        }

        _commandService.Execute<IOpenBrowserCommand>(command =>
        {
            command.URL = url;
        });
    }
}
