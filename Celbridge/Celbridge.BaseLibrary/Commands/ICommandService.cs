namespace Celbridge.BaseLibrary.Commands;

/// <summary>
/// Service for managing a command queue.
/// </summary>
public interface ICommandService
{
    /// <summary>
    /// Starts the service executing commands.
    /// </summary>
    public void StartExecutingCommands();

    /// <summary>
    /// Stop the service executing commands.
    /// </summary>
    public void StopExecutingCommands();

    /// <summary>
    /// Register an executor that can execute commands.
    /// </summary>
    public Result RegisterExecutor(ICommandExecutor commandExecutor);

    /// <summary>
    /// Unregister an executor that can execute commands.
    /// </summary>
    public Result UnregisterExecutor(ICommandExecutor commandExecutor);

    /// <summary>
    /// Execute a command.
    /// The command is added to the command queue and executed when it reaches the front of the queue.
    /// </summary>
    public Result ExecuteCommand(CommandBase command);

    /// <summary>
    /// Execute a command after a delay.
    /// Delay is the minimum time (in milliseconds) before the command should execute. 
    /// Actual execution might take longer than the delay time, depending on what other commands are ahead of it in the queue.
    /// </summary>
    public Result ExecuteCommand(CommandBase command, uint Delay);

    /// <summary>
    /// Undo a previously executed command.
    /// </summary>
    public Result UndoCommand(CommandBase command);

    /// <summary>
    /// Redo a previously executed command.
    /// </summary>
    public Result RedoCommand(CommandBase command);

    /// <summary>
    /// Cancel a pending or in-progress command.
    /// </summary>
    public Result CancelCommand(CommandId commandId);
}
