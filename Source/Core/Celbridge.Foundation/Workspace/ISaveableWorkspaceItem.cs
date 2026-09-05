namespace Celbridge.Workspace;

/// <summary>
/// A workspace item the save tick can flush: an open document, or a utility. Both buffer their edits and
/// write them out once a save timer expires, so the tick treats them the same way.
/// </summary>
public interface ISaveableWorkspaceItem
{
    /// <summary>
    /// The file resource the item's content is stored in.
    /// </summary>
    ResourceKey FileResource { get; }

    /// <summary>
    /// Whether the item holds edits that have not been written to its file resource yet.
    /// </summary>
    bool HasUnsavedChanges { get; }

    /// <summary>
    /// The item's writable state, or the reason it is non-editable.
    /// </summary>
    WritableState WritableState { get; }

    /// <summary>
    /// The item may use a save timer to avoid writing to disk too frequently.
    /// Returns true when the timer has expired, and the file should now be saved.
    /// Fails if HasUnsavedChanges is false.
    /// </summary>
    Result<bool> UpdateSaveTimer(double deltaTime);

    /// <summary>
    /// Writes the item's content to its file resource.
    /// </summary>
    Task<Result> SaveAsync();
}
