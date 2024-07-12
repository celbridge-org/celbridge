using Celbridge.BaseLibrary.Logging;
using Celbridge.BaseLibrary.Messaging;
using System.Diagnostics;

namespace Celbridge.Commands.Services;

public class CommandService : ICommandService
{
    private readonly IMessengerService _messengerService;
    private readonly ILoggingService _loggingService;

    // ExecutionTime is the time in milliseconds when the command should be executed
    private record QueuedCommand(IExecutableCommand Command, long ExecutionTime, bool IsUndoCommand);

    private readonly List<QueuedCommand> _commandQueue = new();

    private object _lock = new object();

    private readonly Stopwatch _stopwatch = new();

    private bool _stopped = false;

    private CommandStack _commandStack = new ();

    public CommandService(
        IMessengerService messengerService,
        ILoggingService loggingService)
    {
        _messengerService = messengerService;
        _loggingService = loggingService;
    }

    public Result Execute<T>(Action<T> configure) where T : IExecutableCommand
    {
        return Execute(configure, 0);
    }

    public Result Execute<T>(Action<T> configure, uint delay) where T : IExecutableCommand
    {
        var command = CreateCommand<T>();
        configure.Invoke(command);
        return EnqueueCommand(command, delay);
    }

    public Result Execute<T>() where T : IExecutableCommand
    {
        return Execute<T>(0);
    }

    public Result Execute<T>(uint delay) where T : IExecutableCommand
    {
        var command = CreateCommand<T>();
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
        if (command.StackName != CommandStackNames.None)
        {
            // Executing a regular command (as opposed to an undo or redo) clears the redo stack.
            _commandStack.ClearRedoCommands(command.StackName);
        }

        return EnqueueCommandInternal(command, delay, false);
    }

    private Result EnqueueCommandInternal(IExecutableCommand command, uint delay, bool IsUndoCommand)
    {
        lock (_lock)
        {
            if (_commandQueue.Any((item) => item.Command.Id == command.Id))
            {
                return Result.Fail($"Command '{command.Id}' is already in the execution queue");
            }

            long executionTime = _stopwatch.ElapsedMilliseconds + delay;
            _commandQueue.Add(new QueuedCommand(command, executionTime, IsUndoCommand));
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

    public string ActiveCommandStack { get; set; } = CommandStackNames.None;

    public bool IsUndoStackEmpty(string stackName)
    {
        lock (_lock)
        {
            return _commandStack.GetUndoCount(stackName) == 0;
        }
    }

    public bool IsRedoStackEmpty(string stackName)
    {
        lock (_lock)
        {
            return _commandStack.GetRedoCount(stackName) == 0;
        }
    }

    public Result Undo(string stackName)
    {
        lock (_lock)
        {
            if (IsUndoStackEmpty(stackName))
            {
                return Result.Fail($"Failed to undo command. Undo stack '{stackName}' is empty");
            }

            // Pop command from the undo queue
            var popResult = _commandStack.PopUndoCommand(stackName);
            if (popResult.IsFailure)
            {
                return popResult;
            }
            var command = popResult.Value;

            // Enqueue this command as an undo. 
            // I'm assuming here that undos should always execute without delay, may not be correct.
            var enqueueResult = EnqueueCommandInternal(command, 0, true);
            if (enqueueResult.IsFailure)
            {
                return enqueueResult;
            }
        }

        return Result.Ok();
    }

    public Result Redo(string stackName)
    {
        lock (_lock)
        {
            if (IsRedoStackEmpty(stackName))
            {
                return Result.Fail($"Failed to redo command. Redo stack '{stackName}' is empty");
            }

            // Pop command from the redo queue
            var popResult = _commandStack.PopRedoCommand(stackName);
            if (popResult.IsFailure)
            {
                return popResult;
            }
            var command = popResult.Value;

            // Enqueue this command as a redo (same as a normal execution).
            // I'm assuming here that redos should always execute without delay, may not be correct.
            var enqueueResult = EnqueueCommandInternal(command, 0, false);
            if (enqueueResult.IsFailure)
            {
                return enqueueResult;
            }
        }

        return Result.Ok();
    }

    public Result<bool> TryUndo()
    {
        string commandStack = ActiveCommandStack;
        if (commandStack == CommandStackNames.None)
        {
            return Result<bool>.Ok(false);
        }
                
        if (IsUndoStackEmpty(commandStack))
        {
            return Result<bool>.Ok(false);
        }

        var undoResult = Undo(commandStack);
        if (undoResult.IsFailure)
        {
            return Result<bool>.Fail($"Failed to undo using command stack '{ActiveCommandStack}'. {undoResult.Error}");
        }

        return Result<bool>.Ok(true);
    }

    public Result<bool> TryRedo()
    {
        string commandStack = ActiveCommandStack;
        if (commandStack == CommandStackNames.None)
        {
            return Result<bool>.Ok(false);
        }

        if (IsRedoStackEmpty(commandStack))
        {
            return Result<bool>.Ok(false);
        }

        var redoResult = Redo(commandStack);
        if (redoResult.IsFailure)
        {
            return Result<bool>.Fail($"Failed to redo using command stack '{ActiveCommandStack}'. {redoResult.Error}");
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

            // Find the first command that is ready to execute
            IExecutableCommand? command = null;
            bool isUndoCommand = false;

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
                    isUndoCommand = item.IsUndoCommand;
                    _commandQueue.RemoveAt(commandIndex);
                }
            }

            if (command is not null)
            {
                try
                {
                    if (isUndoCommand)
                    {
                        //
                        // Execute command as an undo
                        //
   
                        var undoResult = await command.UndoAsync();
                        if (undoResult.IsSuccess)
                        {
                            // Push the undone command onto the redo stack
                            _commandStack.PushRedoCommand(command);
                        }
                        else
                        {
                            _loggingService.Error($"Failed to undo command '{command}': {undoResult.Error}");
                        }
                    }
                    else
                    {
                        //
                        // Execute the command
                        //

                        var executeResult = await command.ExecuteAsync();
                        if (executeResult.IsSuccess)
                        {
                            if (command.StackName != CommandStackNames.None)
                            {
                                _commandStack.PushUndoCommand(command);
                            }
                        }
                        else
                        {
                            _loggingService.Error($"Command '{command}' failed: {executeResult.Error}");
                        }
                    }

                    var message = new ExecutedCommandMessage(command, isUndoCommand);
                    _messengerService.Send(message);
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
}
