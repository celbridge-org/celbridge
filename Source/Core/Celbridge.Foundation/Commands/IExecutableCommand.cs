namespace Celbridge.Commands;

/// <summary>
/// A command that can be executed via the command service.
/// </summary>
public interface IExecutableCommand
{
    /// <summary>
    /// Unique identifier for the command.
    /// </summary>
    EntityId CommandId { get; }

    /// <summary>
    /// Flags to configure behaviour when executing the command.
    /// </summary>
    CommandFlags CommandFlags { get; }

    /// <summary>
    /// Describes where in the source code the command was first executed.
    /// </summary>
    string ExecutionSource { get; set; }

    /// <summary>
    /// Called when the command is executed.
    /// This is used internally by the ExecuteAsync() method and should not be used directly.
    /// </summary>
    Action<Result>? OnExecute { get; set; }

    /// <summary>
    /// Execute the command.
    /// </summary>
    Task<Result> ExecuteAsync();
}

/// <summary>
/// A command that produces a typed result value after execution.
/// The result is available via ResultValue after ExecuteAsync() completes successfully.
/// </summary>
public interface IExecutableCommand<TResult> : IExecutableCommand where TResult : notnull
{
    /// <summary>
    /// The result value produced by the command after successful execution.
    /// </summary>
    TResult ResultValue { get; }
}
