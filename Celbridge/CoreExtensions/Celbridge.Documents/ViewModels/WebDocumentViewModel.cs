using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace Celbridge.Documents.ViewModels;

public partial class WebDocumentViewModel : DocumentViewModel
{
    [ObservableProperty]
    private string _source = string.Empty;
    
    public async Task<Result> LoadContent()
    {
        try
        {
            var text = await File.ReadAllTextAsync(FilePath);

            var jo = JObject.Parse(text);

            string url = string.Empty;
            if (jo.TryGetValue("url", out var urlToken))
            {
               Source = urlToken.ToString();
               return Result.Ok();
            }
        }
        catch (Exception ex)
        {
            return Result.Fail(ex, $"An exception occured when loading document from file: {FilePath}");
        }

        return Result.Fail($"Failed to local content from .web file: {FileResource}");
    }
}
