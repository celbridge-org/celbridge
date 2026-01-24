namespace Celbridge.UserInterface;

/// <summary>
/// Provides support for undoing and redoing operations.
/// </summary>
public interface IUndoService
{
    /// <summary>
    /// Undo the last operation.
    /// </summary>
    Result Undo();

    /// <summary>
    /// Redo the most recently undone operation.
    /// </summary>
    Result Redo();
}
