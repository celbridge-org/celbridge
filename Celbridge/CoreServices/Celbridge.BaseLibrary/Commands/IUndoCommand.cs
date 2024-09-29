using Celbridge.Commands;

namespace Celbridge.BaseLibrary.Commands;

/// <summary>
/// Undo the command in the specified undo stack.
/// </summary>
public interface IUndoCommand : IExecutableCommand
{
    UndoStackName UndoStack { get; set; }
}
