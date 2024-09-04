namespace Celbridge.Documents.Views;

public class DocumentViewFactory
{
    public Result<IDocumentView> CreateDocumentView(DocumentViewType viewType)
    {
        IDocumentView? documentView = null;

        switch (viewType)
        {
            case DocumentViewType.DefaultDocument:
                documentView = new DefaultDocumentView();
                break;
            case DocumentViewType.TextDocument:
                documentView = new TextDocumentView();
                break;
            case DocumentViewType.WebDocument:
                documentView = new WebDocumentView();
                break;
            case DocumentViewType.WebViewer:
                // Todo: Implement viewer type
                break;
        }

        if (documentView is null)
        {
            return Result<IDocumentView>.Fail($"Failed to create document view for document type: '{viewType}'");
        }

        return Result<IDocumentView>.Ok(documentView);
    }
}
