using Celbridge.Commands;

namespace Celbridge.BaseLibrary.Commands;

/// <summary>
/// Redo the command in the specified undo stack.
/// </summary>
public interface IRedoCommand : IExecutableCommand
{
    UndoStackName UndoStack { get; set; }
}
