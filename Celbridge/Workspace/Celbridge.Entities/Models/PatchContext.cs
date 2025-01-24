namespace Celbridge.Entities.Models;

/// <summary>
/// Describes the context in which a patch is applied.
/// </summary>
public enum PatchContext
{
    /// <summary>
    /// Modifying an entity.
    /// </summary>
    Modify,

    /// <summary>
    /// Undoing a previously applied modification.
    /// </summary>
    Undo,

    /// <summary>
    /// Redoing a previously undone modification.
    /// </summary>
    Redo
}
