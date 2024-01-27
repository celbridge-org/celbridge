namespace Celbridge.Legacy.Models;

// An entity that may be opened as a document
public interface IDocumentEntity : IEntity
{}

public interface IDocument
{
    public IDocumentEntity DocumentEntity { get; }
}

public class Document : IDocument
{
    public IDocumentEntity DocumentEntity { get; }

    public Document(IDocumentEntity documentEntity) 
    {
        DocumentEntity = documentEntity;
    }
}
