using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Workspace;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Celbridge.Commands.Services;

using ICommandLogger = Logging.ILogger<CommandService>;
using Path = System.IO.Path;

public class CommandService : ICommandService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICommandLogger _logger;
    private readonly ILogSerializer _logSerializer;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    private record QueuedCommand(IExecutableCommand Command, CommandExecutionMode ExecutionMode);

    private readonly List<QueuedCommand> _commandQueue = new();

    private object _lock = new object();

    private readonly Stopwatch _stopwatch = new();
    private double _lastWorkspaceUpdateTime = 0;

    private bool _stopped = false;

    private UndoStack _undoStack = new ();

    public CommandService(
        IServiceProvider serviceProvider,
        ICommandLogger logger,
        ILogSerializer logSerializer,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _logSerializer = logSerializer;
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
    }

    public Result Execute<T>(
        Action<T>? configure = null,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0) where T : IExecutableCommand
    {
        var command = CreateCommand<T>();
        command.ExecutionSource = $"{Path.GetFileName(filePath)}:{lineNumber}";

        // Configure the command if the caller provided a configuration action
        configure?.Invoke(command);

        return EnqueueCommand(command, CommandExecutionMode.Execute);
    }

    public async Task<Result> ExecuteImmediate<T>(
        Action<T>? configure = null,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0) where T : IExecutableCommand
    {
        var command = CreateCommand<T>();
        command.ExecutionSource = $"{Path.GetFileName(filePath)}:{lineNumber}";

        // Configure the command if the caller provided a configuration action
        configure?.Invoke(command);

        return await command.ExecuteAsync();
    }

    public async Task<Result> ExecuteAsync<T>(
        Action<T>? configure = null,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0) where T : IExecutableCommand
    {
        var command = CreateCommand<T>();
        command.ExecutionSource = $"{Path.GetFileName(filePath)}:{lineNumber}";

        // Configure the command if the caller provided a configuration action
        configure?.Invoke(command);

        var tcs = new TaskCompletionSource();

        // Set a callback for when the command is executed
        Result executionResult = Result.Fail();
        command.OnExecute = (result) =>
        {
            executionResult = result;
            tcs.TrySetResult();
        };

        var enqueueResult = EnqueueCommand(command, CommandExecutionMode.Execute);
        if (enqueueResult.IsFailure)
        {
            return Result.Fail($"Failed to enqueue command")
                .WithErrors(enqueueResult);
        }

        // Wait for the command to execute
        await tcs.Task;

        // Clear the callback to avoid it being called again via undo/redo.
        command.OnExecute = null;

        if (executionResult.IsFailure)
        {
            return Result.Fail($"Command execution failed")
                .WithErrors(executionResult);
        }

        return Result.Ok();
    }

    public bool ContainsCommandsOfType<T>() where T : notnull
    {
        lock (_lock)
        {
            return _commandQueue.Any(o => o is T);
        }
    }

    public void RemoveCommandsOfType<T>() where T : notnull
    {
        lock (_lock)
        {
            _commandQueue.RemoveAll(c => c.GetType().IsAssignableTo(typeof(T)));
        }
    }

    public int GetUndoCount()
    {
        lock (_lock)
        {
            return _undoStack.GetUndoCount();
        }
    }

    public int GetRedoCount()
    {
        lock (_lock)
        {
            return _undoStack.GetRedoCount();
        }
    }

    public Result Undo()
    {
        lock (_lock)
        {
            if (GetUndoCount() == 0)
            {
                // Noop
                return Result.Ok();
            }

            // Pop next command(s) from the undo queue
            var popResult = _undoStack.PopCommands(UndoStackOperation.Undo);
            if (popResult.IsFailure)
            {
                return Result.Fail("Failed to pop command from undo stack")
                    .WithErrors(popResult);
            }
            var commandList = popResult.Value;

            foreach (var command in commandList)
            {
                // Enqueue this command as an undo. 
                var enqueueResult = EnqueueCommand(command, CommandExecutionMode.Undo);
                if (enqueueResult.IsFailure)
                {
                    return Result.Fail("Failed to enqueue popped undo command")
                        .WithErrors(enqueueResult);
                }
            }
        }

        return Result.Ok();
    }

    public Result Redo()
    {
        lock (_lock)
        {
            if (GetRedoCount() == 0)
            {
                // Noop
                return Result.Ok();
            }

            // Pop next command(s) from the redo queue
            var popResult = _undoStack.PopCommands(UndoStackOperation.Redo);
            if (popResult.IsFailure)
            {
                return Result.Fail("Failed to pop command from redo stack")
                    .WithErrors(popResult);
            }
            var commandList = popResult.Value;

            foreach (var command in commandList)
            {
                // Enqueue this command as a redo (same as a normal execution).
                var enqueueResult = EnqueueCommand(command, CommandExecutionMode.Redo);
                if (enqueueResult.IsFailure)
                {
                    return Result.Fail("Failed to enqueue popped undo command")
                        .WithErrors(enqueueResult);
                }
            }
        }

        return Result.Ok();
    }

    public void StartExecution()
    {
        _ = StartExecutionAsync();
    }

    public void StopExecution()
    {
        _stopped = true;
    }

    private async Task StartExecutionAsync()
    {
        _stopwatch.Start();

        while (true)
        {
            if (_stopped)
            {
                lock (_commandQueue)
                {
                    _commandQueue.Clear();
                }
                _stopped = false;
                break;
            }

            // To avoid race conditions, the workspace state is updated while there are no executing commands.
            // This ensures that no commands are executed until resource and entity saving completes.
            var updateWorkspaceResult = await UpdateWorkspaceAsync();
            if (updateWorkspaceResult.IsFailure)
            {
                _logger.LogError(updateWorkspaceResult, "Failed to update workspace");
            }

            // Find the first command that is ready to execute
            IExecutableCommand? command = null;
            var executionMode = CommandExecutionMode.Execute;

            lock (_lock)
            {
                if (_commandQueue.Count > 0)
                {
                    var item = _commandQueue[0];
                    command = item.Command;
                    executionMode = item.ExecutionMode;
                    _commandQueue.RemoveAt(0);
                }
            }

            if (command is not null)
            {
                try
                {
                    // Notify listeners that the command is about to execute
                    var startedMessage = new ExecuteCommandStartedMessage(command, executionMode, (float)_stopwatch.Elapsed.TotalSeconds);
                    _messengerService.Send(startedMessage);

                    var scopeName = $"{executionMode} {command.GetType().Name}";
                    using (_logger.BeginScope(scopeName))
                    {
                        // Log the command execution at the debug level
                        string logEntry = _logSerializer.SerializeObject(startedMessage, false);
                        _logger.LogDebug(logEntry);

                        if (executionMode == CommandExecutionMode.Undo)
                        {
                            //
                            // Execute command as an undo
                            //

                            var undoResult = await command.UndoAsync();

                            if (undoResult.IsSuccess)
                            {
                                // Push the undone command onto the redo stack
                                _undoStack.PushRedoCommand(command);
                            }
                            else
                            {
                                _logger.LogError(undoResult, "Undo command failed");
                            }
                        }
                        else
                        {
                            //
                            // Execute the command as a regular execution or redo
                            //

                            var executeResult = await command.ExecuteAsync();

                            if (executeResult.IsSuccess)
                            {
                                if (command.CommandFlags.HasFlag(CommandFlags.Undoable))
                                {
                                    _undoStack.PushUndoCommand(command);
                                }
                            }
                            else
                            {
                                _logger.LogError(executeResult, "Execute command failed");
                            }

                            // Call the OnExecute callback if it is set.
                            // This is used by the ExecuteAsync() methods to notify the caller about the execution.
                            command.OnExecute?.Invoke(executeResult);
                        }

                        // Update the resource registry if the command requires it.
                        if (_workspaceWrapper.IsWorkspacePageLoaded &&
                            command.CommandFlags.HasFlag(CommandFlags.UpdateResources))
                        {
                            var updateResult = await UpdateResourcesAsync(command);
                            if (updateResult.IsFailure)
                            {
                                _logger.LogError(updateResult, "Update resources failed");
                            }
                        }

                        // Save the workspace state if the command requires it.
                        if (_workspaceWrapper.IsWorkspacePageLoaded &&
                            command.CommandFlags.HasFlag(CommandFlags.SaveWorkspaceState))
                        {
                            _workspaceWrapper.WorkspaceService.SetWorkspaceStateIsDirty();
                        }

                        var endedMessage = new ExecuteCommandEndedMessage(command, executionMode, (float)_stopwatch.Elapsed.TotalSeconds);
                        _messengerService.Send(endedMessage);
                    }
                }
                catch (Exception ex)
                {
                    // I decided not to localize this because exceptions should never occur. This is not text that the
                    // user is expected to ever see.
                    _logger.LogError(ex, $"An exception occurred when executing the command. Check the log file for more information.");
                }
            }

            await Task.Delay(1);
        }
    }

    private T CreateCommand<T>() where T : IExecutableCommand
    {
        T command = _serviceProvider.GetRequiredService<T>();

        return command;
    }

    private Result EnqueueCommand(IExecutableCommand command, CommandExecutionMode executionMode)
    {
        lock (_lock)
        {
            if (executionMode == CommandExecutionMode.Execute)
            {
                // Executing a regular command (as opposed to an undo or redo) clears the redo stack.
                _undoStack.ClearRedoCommands();
            }

            if (_commandQueue.Any((item) => item.Command.CommandId == command.CommandId))
            {
                return Result.Fail($"Command '{command.CommandId}' is already in the execution queue");
            }

            _commandQueue.Add(new QueuedCommand(command, executionMode));
        }

        return Result.Ok();
    }

    /// <summary>
    /// Flush any pending save operations before the next command executes.
    /// </summary>
    private async Task<Result> UpdateWorkspaceAsync()
    {
        var now = _stopwatch.Elapsed.TotalSeconds;

        if (!_workspaceWrapper.IsWorkspacePageLoaded ||
            _lastWorkspaceUpdateTime == 0)
        {
            // No workspace is loaded, or this is the first call so we can't calculate a delta time.
            _lastWorkspaceUpdateTime = now;
            return Result.Ok();
        }

        var deltaTime = now - _lastWorkspaceUpdateTime;

        var updateResult = await _workspaceWrapper.WorkspaceService.UpdateWorkspaceAsync(deltaTime);
        if (updateResult.IsFailure)
        {
            return Result.Fail($"Failed to update workspace state")
                .WithErrors(updateResult);
        }

        // Use the latest reported time to account for any time spent saving
        _lastWorkspaceUpdateTime = _stopwatch.Elapsed.TotalSeconds;

        return Result.Ok();
    }

    private async Task<Result> UpdateResourcesAsync(IExecutableCommand command)
    {
        // For grouped commands, only the last command to execute should perform the
        // resource update to avoid unnecessary updates.
        // Every non-grouped command does update the resource registry. This ensures
        // that the registry & view are up to date before the next command in the queue executes.

        foreach (var item in _commandQueue)
        {
            if (item.Command.CommandFlags.HasFlag(CommandFlags.UpdateResources) &&
                item.Command.UndoGroupId == command.UndoGroupId)
            {
                return Result.Ok();
            }
        }

        var explorerService = _workspaceWrapper.WorkspaceService.ExplorerService;
        var updateResult = await explorerService.UpdateResourcesAsync();
        if (updateResult.IsFailure)
        {
            return updateResult;
        }

        return Result.Ok();
    }
}
