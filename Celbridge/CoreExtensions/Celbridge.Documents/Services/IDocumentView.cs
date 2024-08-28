namespace Celbridge.Documents.Services;

/// <summary>
/// Interface for interacting with a document view.
/// </summary>
public interface IDocumentView
{
    /// <summary>
    /// Flag that indicates if the document has been modified and requires saving.
    /// </summary>
    bool IsDirty { get; }

    /// <summary>
    /// Save the document to disk.
    /// </summary>
    Task<Result> SaveDocument();
}
