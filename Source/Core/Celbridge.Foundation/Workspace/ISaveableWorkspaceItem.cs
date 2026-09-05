namespace Celbridge.Workspace;

/// <summary>
/// A workspace item that buffers edits and writes them to its file resource when its save timer expires.
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
    /// Advances the item's save timer. Returns true when the timer has expired and the file should now be
    /// saved. Fails if HasUnsavedChanges is false.
    /// </summary>
    Result<bool> UpdateSaveTimer(double deltaTime);

    /// <summary>
    /// Writes the item's content to its file resource.
    /// </summary>
    Task<Result> SaveAsync();
}
