namespace Celbridge.Commands;

/// <summary>
/// Names of undo stacks used to support undo/redo in the application.
/// </summary>
public enum UndoStackName
{
    None,
    Explorer,
    Document,
    Inspector
}
