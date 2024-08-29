namespace Celbridge.Documents;

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
    /// Returns true if the document can be closed.
    /// For example, this could be used to prompt the user to save changes before closing.
    /// </summary>
    Task<bool> CanCloseDocument();

    /// <summary>
    /// Save the document to disk.
    /// </summary>
    Task<Result> SaveDocument();
}
