namespace Celbridge.Workspace;

/// <summary>
/// The kind of state a status pip marks. The pip shows each kind in its own color.
/// </summary>
public enum PipKind
{
    /// <summary>
    /// A neutral state, like a sync under way.
    /// </summary>
    Neutral,

    /// <summary>
    /// A state that went well, like a finished task.
    /// </summary>
    Success,

    /// <summary>
    /// Something the user should look at, like a problem that does not block their work.
    /// </summary>
    Notice,

    /// <summary>
    /// A state the user must keep in mind, like a recording.
    /// </summary>
    Alert
}

/// <summary>
/// A workspace item's status pip, set by the page of the item's custom editor. The label is one line, already
/// localized by the page. A pulsing pip fades out and back in every few seconds.
/// </summary>
public sealed record WorkspaceItemPip(PipKind Kind, string Label, bool IsPulsing);

/// <summary>
/// A document or utility open in the workspace.
/// </summary>
public interface IWorkspaceItem
{
    /// <summary>
    /// The file resource the item's content is stored in.
    /// </summary>
    ResourceKey FileResource { get; }

    /// <summary>
    /// Whether the item holds edits that have not been written to its file resource yet. Stays true when a
    /// write fails.
    /// </summary>
    bool HasUnsavedChanges { get; }

    /// <summary>
    /// The item's writable state, or the reason it is non-editable.
    /// </summary>
    WritableState WritableState { get; }

    /// <summary>
    /// The item's status pip, or null when the item's editor has set none.
    /// </summary>
    WorkspaceItemPip? Pip { get; }

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
