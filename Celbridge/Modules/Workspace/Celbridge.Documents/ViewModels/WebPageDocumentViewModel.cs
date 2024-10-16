using Celbridge.Commands;
using Celbridge.Explorer;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace Celbridge.Documents.ViewModels;

public partial class WebPageDocumentViewModel : DocumentViewModel
{
    private readonly ICommandService _commandService;

    [ObservableProperty]
    private string _sourceURL = string.Empty;

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

            var jo = JObject.Parse(text);

            string url = string.Empty;
            if (jo.TryGetValue("url", out var urlToken))
            {
               SourceURL = urlToken.ToString();
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
