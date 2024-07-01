namespace Celbridge.BaseLibrary.Commands;

/// <summary>
/// Names of command stacks used to support undo/redo.
/// </summary>
public static class CommandStackNames
{
    public const string None = $"{nameof(None)}";
    public const string Project = $"{nameof(Project)}";
    public const string Document = $"{nameof(Document)}";
    public const string Inspector = $"{nameof(Inspector)}";
}