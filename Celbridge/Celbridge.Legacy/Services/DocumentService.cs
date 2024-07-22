using Celbridge.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Celbridge.Legacy.Services;

public interface IDocumentService
{
    public Result OpenDocument(IDocumentEntity documentEntity);
    public Result CloseDocument(IDocumentEntity documentEntity, bool autoReload);
    public Result CloseAllDocuments(bool autoReload);
}

public class DocumentOpenedMessage : ValueChangedMessage<IDocument>
{
    public DocumentOpenedMessage(IDocument document) : base(document)
    {}
}

public class DocumentClosedMessage : ValueChangedMessage<IDocument>
{
    public bool AutoReload { get; set; }

    public DocumentClosedMessage(IDocument document, bool autoReload) : base(document)
    {
        AutoReload = autoReload;
    }
}

public class DocumentService : IDocumentService
{
    private readonly IMessengerService _messengerService;
    private readonly List<IDocument> _openDocuments = new();

    public DocumentService(IMessengerService messengerService)
    {
        _messengerService = messengerService;
    }

    public Result OpenDocument(IDocumentEntity documentEntity)
    {
        Guard.IsNotNull(documentEntity);

        IDocument? openedDocument = null;
        foreach (var document in _openDocuments)
        {
            if (document.DocumentEntity == documentEntity)
            {
                openedDocument = document;
                break;
            }
        }

        if (openedDocument == null)
        {
            openedDocument = new Document(documentEntity);
            _openDocuments.Add(openedDocument);
        }

        _messengerService.Send(new DocumentOpenedMessage(openedDocument));
        return new SuccessResult();
    }

    public Result CloseDocument(IDocumentEntity documentEntity, bool autoReload)
    {
        Guard.IsNotNull(documentEntity);

        IDocument? document = null;
        foreach (var d in _openDocuments)
        {
            if (d.DocumentEntity == documentEntity)
            {
                document = d;
                break;
            }
        }

        if (document == null)
        {
            // Closing a document that isn't open is just a noop
            return new SuccessResult();
        }

        Guard.IsTrue(_openDocuments.Contains(document));

        _openDocuments.Remove(document);

        var message = new DocumentClosedMessage(document, autoReload);
        _messengerService.Send(message);

        return new SuccessResult();
    }

    public Result CloseAllDocuments(bool autoReload)
    {
        // Take a copy of the list of open documents so that we can modify it while we iterate
        var documents = new List<IDocument>(_openDocuments);

        foreach (var document in documents)
        {
            var result = CloseDocument(document.DocumentEntity, autoReload);
            if (result.Failure)
            {
                return result;
            }
        }

        return new SuccessResult();
    }
}
