namespace Celbridge.Commands;

/// <summary>
/// Message sent when a command is about to execute.
/// </summary>
public record CommandExecutingMessage(IExecutableCommand Command, CommandExecutionMode ExecutionMode, float ElapsedTime);