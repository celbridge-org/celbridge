namespace Celbridge.Commands;

/// <summary>
/// Names of undo stacks used to support undo/redo in the application.
/// </summary>
public static class UndoStackNames
{
    public const string None = $"{nameof(None)}";
    public const string Project = $"{nameof(Project)}";
    public const string Document = $"{nameof(Document)}";
    public const string Inspector = $"{nameof(Inspector)}";
}