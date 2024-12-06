namespace Celbridge.UserInterface;

/// <summary>
/// Provides support for undoing and redoing operations.
/// </summary>
public interface IUndoService
{
    /// <summary>
    /// Attempt to undo the last operation.
    /// Returns false if there was no operation on the undo stack to undo.
    /// </summary>
    Result<bool> Undo();

    /// <summary>
    /// Attempt to redo the most recently undone operation.
    /// Returns false if there was no operation on the redo stack to redo.
    /// </summary>
    Result<bool> Redo();
}
