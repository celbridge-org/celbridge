namespace Celbridge.BaseLibrary.Commands;

/// <summary>
/// Sent when a command has been executed or undone.
/// </summary>
public record ExecutedCommandMessage(IExecutableCommand Command, bool IsUndo);