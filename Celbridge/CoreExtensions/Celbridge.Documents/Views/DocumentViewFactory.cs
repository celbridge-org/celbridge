using Celbridge.Resources;

using Path = System.IO.Path;

namespace Celbridge.Documents.Views;

public class DocumentViewFactory
{
    public Result<Control> CreateDocumentView(ResourceKey fileResource, string filePath)
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
            // Todo: Read the file contents to initialize the text editor
            documentView = new TextDocumentView();
        }

        if (documentView is null)
        {
            return Result<Control>.Fail($"Failed to create document view for file resource: '{fileResource}'");
        }

        return Result<Control>.Ok(documentView);
    }
}
