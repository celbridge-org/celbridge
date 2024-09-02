using Celbridge.Explorer;

using Path = System.IO.Path;

namespace Celbridge.Documents.Views;

public class DocumentViewFactory
{
    public async Task<Result<Control>> CreateDocumentView(ResourceKey fileResource, string filePath)
    {
        Control? documentView;

        var extension = Path.GetExtension(filePath);
        if (extension == ".web")
        {
            // Todo: Read the URL from the resource properties
            documentView = new WebDocumentView();
        }
        else
        {
            var textDocumentView = new TextDocumentView();

            var loadResult = await textDocumentView.ViewModel.LoadDocument(filePath);
            if (loadResult.IsFailure)
            {
                var failure = Result<Control>.Fail($"Failed to create document view for file resource: '{fileResource}'");
                failure.MergeErrors(loadResult);
                return failure;
            }

            documentView = textDocumentView;
        }

        if (documentView is null)
        {
            return Result<Control>.Fail($"Failed to create document view for file resource: '{fileResource}'");
        }

        return Result<Control>.Ok(documentView);
    }
}
