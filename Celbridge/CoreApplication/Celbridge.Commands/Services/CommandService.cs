using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Workspace;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Celbridge.Commands.Services;

public class CommandService : ICommandService
{
    private const long SaveWorkspaceDelay = 250; // ms

    private readonly IMessengerService _messengerService;
    private readonly ILoggingService _loggingService;

    // ExecutionTime is the time in milliseconds when the command should be executed
    private record QueuedCommand(IExecutableCommand Command, long ExecutionTime, CommandExecutionMode ExecutionMode);

    private readonly List<QueuedCommand> _commandQueue = new();

    private object _lock = new object();

    private readonly Stopwatch _stopwatch = new();

    private bool _stopped = false;

    private UndoStack _undoStack = new ();

    private long _saveWorkspaceTime;

    public CommandService(
        IMessengerService messengerService,
        ILoggingService loggingService)
    {
        _messengerService = messengerService;
        _loggingService = loggingService;
    }

    public Result Execute<T>
    (
        Action<T> configure,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0
    ) where T : IExecutableCommand
    {
        var command = CreateCommand<T>();
        command.ExecutionSource = $"{Path.GetFileName(filePath)}:{lineNumber}";
        configure.Invoke(command);
        return EnqueueCommand(command, 0);
    }

    public Result Execute<T>
    (
        Action<T> configure,
        uint delay,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0
    ) where T : IExecutableCommand
    {
        var command = CreateCommand<T>();
        command.ExecutionSource = $"{Path.GetFileName(filePath)}:{lineNumber}";
        configure.Invoke(command);
        return EnqueueCommand(command, delay);
    }

    public Result Execute<T>
    (
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0
    ) where T : IExecutableCommand
    {
        var command = CreateCommand<T>();
        command.ExecutionSource = $"{Path.GetFileName(filePath)}:{lineNumber}";
        return EnqueueCommand(command, 0);
    }

    public Result Execute<T>
    (
        uint delay,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0
    ) where T : IExecutableCommand
    {
        var command = CreateCommand<T>();
        command.ExecutionSource = $"{Path.GetFileName(filePath)}:{lineNumber}";
        return EnqueueCommand(command, delay);
    }

    public T CreateCommand<T>() where T : IExecutableCommand
    {
        var serviceProvider = ServiceLocator.ServiceProvider;
        T command = serviceProvider.GetRequiredService<T>();

        return command;
    }

    public Result EnqueueCommand(IExecutableCommand command)
    {
        return EnqueueCommand(command, 0);
    }

    public Result EnqueueCommand(IExecutableCommand command, uint delay)
    {
        if (command.UndoStackName != UndoStackNames.None)
        {
            // Executing a regular command (as opposed to an undo or redo) clears the redo stack.
            _undoStack.ClearRedoCommands(command.UndoStackName);
        }

        return EnqueueCommandInternal(command, delay, CommandExecutionMode.Execute);
    }

    private Result EnqueueCommandInternal(IExecutableCommand command, uint delay, CommandExecutionMode ExecutionMode)
    {
        lock (_lock)
        {
            if (_commandQueue.Any((item) => item.Command.CommandId == command.CommandId))
            {
                return Result.Fail($"Command '{command.CommandId}' is already in the execution queue");
            }

            long executionTime = _stopwatch.ElapsedMilliseconds + delay;
            _commandQueue.Add(new QueuedCommand(command, executionTime, ExecutionMode));
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

    public string ActiveUndoStack { get; set; } = UndoStackNames.None;

    public bool IsUndoStackEmpty(string undoStackName)
    {
        lock (_lock)
        {
            return _undoStack.GetUndoCount(undoStackName) == 0;
        }
    }

    public bool IsRedoStackEmpty(string undoStackName)
    {
        lock (_lock)
        {
            return _undoStack.GetRedoCount(undoStackName) == 0;
        }
    }

    public Result Undo(string undoStackName)
    {
        lock (_lock)
        {
            if (IsUndoStackEmpty(undoStackName))
            {
                return Result.Fail($"Failed to undo command. Undo stack '{undoStackName}' is empty");
            }

            // Pop next command(s) from the undo queue
            var popResult = _undoStack.PopCommands(undoStackName, UndoStackOperation.Undo);
            if (popResult.IsFailure)
            {
                return popResult;
            }
            var commandList = popResult.Value;

            foreach (var command in commandList)
            {
                // Enqueue this command as an undo. 
                // I'm assuming here that undos should always execute without delay, which may not be correct.
                var enqueueResult = EnqueueCommandInternal(command, 0, CommandExecutionMode.Undo);
                if (enqueueResult.IsFailure)
                {
                    return enqueueResult;
                }
            }
        }

        return Result.Ok();
    }

    public Result Redo(string undoStackName)
    {
        lock (_lock)
        {
            if (IsRedoStackEmpty(undoStackName))
            {
                return Result.Fail($"Failed to redo command. Redo stack '{undoStackName}' is empty");
            }

            // Pop next command(s) from the redo queue
            var popResult = _undoStack.PopCommands(undoStackName, UndoStackOperation.Redo);
            if (popResult.IsFailure)
            {
                return popResult;
            }
            var commandList = popResult.Value;

            foreach (var command in commandList)
            {
                // Enqueue this command as a redo (same as a normal execution).
                // I'm assuming here that redos should always execute without delay, which may not be correct.
                var enqueueResult = EnqueueCommandInternal(command, 0, CommandExecutionMode.Redo);
                if (enqueueResult.IsFailure)
                {
                    return enqueueResult;
                }
            }
        }

        return Result.Ok();
    }

    public Result<bool> TryUndo()
    {
        string undoStackName = ActiveUndoStack;
        if (undoStackName == UndoStackNames.None)
        {
            return Result<bool>.Ok(false);
        }
                
        if (IsUndoStackEmpty(undoStackName))
        {
            return Result<bool>.Ok(false);
        }

        var undoResult = Undo(undoStackName);
        if (undoResult.IsFailure)
        {
            return Result<bool>.Fail($"Failed to undo using undo stack '{ActiveUndoStack}'. {undoResult.Error}");
        }

        return Result<bool>.Ok(true);
    }

    public Result<bool> TryRedo()
    {
        string undoStackName = ActiveUndoStack;
        if (undoStackName == UndoStackNames.None)
        {
            return Result<bool>.Ok(false);
        }

        if (IsRedoStackEmpty(undoStackName))
        {
            return Result<bool>.Ok(false);
        }

        var redoResult = Redo(undoStackName);
        if (redoResult.IsFailure)
        {
            return Result<bool>.Fail($"Failed to redo using undo stack '{ActiveUndoStack}'. {redoResult.Error}");
        }

        return Result<bool>.Ok(true);
    }

    public void StartExecution()
    {
        _ = StartExecutionAsync();
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

            var currentTime = _stopwatch.ElapsedMilliseconds;

            // Save the workspace state if a command has requested it
            // Handling it here ensures the save is performed while no command is executing.
            if (_saveWorkspaceTime > 0 &&
                currentTime > _saveWorkspaceTime)
            {
                // Todo: Call Save directly on the Workspace service instead of sending message
                var message = new RequestSaveWorkspaceStateMessage();
                _messengerService.Send(message);
                _saveWorkspaceTime = 0; // Reset the timer
            }

            // Find the first command that is ready to execute
            IExecutableCommand? command = null;
            var executionMode = CommandExecutionMode.Execute;

            lock (_lock)
            {
                int commandIndex = -1;
                for (int i = 0; i < _commandQueue.Count; i++)
                {
                    var queuedCommand = _commandQueue[i];
                    var commandExecutionTime = queuedCommand.ExecutionTime;
                    if (currentTime >= commandExecutionTime)
                    {
                        commandIndex = i;
                        break;
                    }
                }

                if (commandIndex > -1)
                {
                    var item = _commandQueue[commandIndex];
                    command = item.Command;
                    executionMode = item.ExecutionMode;
                    _commandQueue.RemoveAt(commandIndex);
                }
            }

            if (command is not null)
            {
                try
                {
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
                            _loggingService.Error($"Failed to undo command '{command}': {undoResult.Error}");
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
                            if (command.UndoStackName != UndoStackNames.None)
                            {
                                _undoStack.PushUndoCommand(command);
                            }
                        }
                        else
                        {
                            _loggingService.Error($"Command '{command}' failed: {executeResult.Error}");
                        }
                    }

                    var message = new ExecutedCommandMessage(command, executionMode, (float)_stopwatch.Elapsed.TotalSeconds);
                    _messengerService.Send(message);

                    // Trigger a resource registry update if needed.
                    CheckUpdateResourceRegistry(command);
                    CheckSaveWorkspaceState(command);
                }
                catch (Exception ex)
                {
                    _loggingService.Error($"Command '{command}' failed. {ex.Message}");
                }
            }

            await Task.Delay(1);
        }
    }

    public void StopExecution()
    {
        _stopped = true;
    }

    private void CheckUpdateResourceRegistry(IExecutableCommand command)
    {
        // Update the resource registry if this command requires it.
        if (!command.CommandFlags.HasFlag(CommandFlags.UpdateResourceRegistry))
        {
            return;
        }

        // For grouped commands, only the last command to execute should request the
        // resource update. Updating resources for the previous commands is redundant.
        // Every non-grouped command should update the resource registry however. This ensures
        // that the registry is up to date when the next command in the queue executes.

        foreach (var item in _commandQueue)
        {
            if (item.Command.CommandFlags.HasFlag(CommandFlags.UpdateResourceRegistry) &&
                item.Command.UndoGroupId == command.UndoGroupId)
            {
                return;
            }
        }

        var requestMessage = new RequestResourceRegistryUpdateMessage();
        _messengerService.Send(requestMessage);
    }

    private void CheckSaveWorkspaceState(IExecutableCommand command)
    {
        // Save the workspace state if this command requires it.
        if (!command.CommandFlags.HasFlag(CommandFlags.SaveWorkspaceState))
        {
            return;
        }

        // Set a timer to save the workspace state after a short delay.
        // If any commands with the SaveWorkspaceState flag are executed before the timer expires,
        // the timer will be extended again. This avoids saving the workspace state too frequently.

        _saveWorkspaceTime = _stopwatch.ElapsedMilliseconds + SaveWorkspaceDelay;
    }
}
