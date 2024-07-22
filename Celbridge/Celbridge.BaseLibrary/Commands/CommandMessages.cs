namespace Celbridge.Commands;

/// <summary>
/// Sent when a command is executed.
/// </summary>
public record ExecutedCommandMessage(IExecutableCommand Command, CommandExecutionMode ExecutionMode, float ElapsedTime);