using Celbridge.Documents.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.FileViewer.ViewModels;

public partial class FileViewerDocumentViewModel : DocumentViewModel
{
    [ObservableProperty]
    private string _source = string.Empty;
    
    public async Task<Result> LoadContent()
    {
        try
        {
            var fileUri = new Uri(FilePath);
            Source = fileUri.ToString();
            await Task.CompletedTask;
 
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when loading document from file: {FilePath}")
                .WithException(ex);
        }
    }
}
