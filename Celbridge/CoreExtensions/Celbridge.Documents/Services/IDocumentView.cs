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
    /// Update the save timer on the document to avoid writing to disk too frequently.
    /// Returns true when the timer has expired.
    /// Fails if the document is not dirty.
    /// </summary>
    Result<bool> UpdateSaveTimer(double deltaTime);

    /// <summary>
    /// Save the document to disk.
    /// </summary>
    Task<Result> SaveDocument();
}
