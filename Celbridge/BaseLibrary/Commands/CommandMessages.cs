namespace Celbridge.Commands;

/// <summary>
/// A message sent when a command is about to execute.
/// </summary>
public record ExecuteCommandStartedMessage(IExecutableCommand Command, CommandExecutionMode ExecutionMode, float ElapsedTime);

/// <summary>
/// A message sent when a command has finished executing.
/// </summary>
public record ExecuteCommandEndedMessage(IExecutableCommand Command, CommandExecutionMode ExecutionMode, float ElapsedTime);

