using Celbridge.BaseLibrary.Commands;
using System.Diagnostics;

namespace Celbridge.Services.Commands;

public class CommandService : ICommandService
{
    private record QueuedCommand(CommandBase Command, long ExecutionTime);

    private readonly List<QueuedCommand> _commandQueue = new();

    private readonly List<ICommandExecutor> _executors = new();

    private object _lock = new object();

    private readonly Stopwatch _stopwatch = new();

    private bool _stopped = false;

    public CommandService()
    {}

    public Result RegisterExecutor(ICommandExecutor commandExecutor)
    {
        if (_executors.Contains(commandExecutor))
        {
            return Result.Fail("Executor already registered");
        }

        _executors.Add(commandExecutor);

        return Result.Ok();
    }

    public Result UnregisterExecutor(ICommandExecutor commandExecutor)
    {
        if (!_executors.Contains(commandExecutor))
        {
            return Result.Fail("Executor is not registered");
        }

        _executors.Remove(commandExecutor);

        return Result.Ok();
    }

    public Result ExecuteCommand(CommandBase command)
    {
        return ExecuteCommand(command, 0);
    }

    public Result ExecuteCommand(CommandBase command, uint delay)
    {
        lock (_lock)
        {
            if (_commandQueue.Any((item) => item.Command.Id == command.Id))
            {
                return Result.Fail($"Command '{command.Id}' is already in the execution queue");
            }

            long executionTime = _stopwatch.ElapsedMilliseconds + delay;
            _commandQueue.Add(new QueuedCommand(command, executionTime));
        }

        return Result.Ok();
    }

    public Result UndoCommand(CommandBase command)
    {
        throw new NotImplementedException();
    }

    public Result RedoCommand(CommandBase command)
    {
        throw new NotImplementedException();
    }

    public Result CancelCommand(CommandId commandId)
    {
        lock (_lock)
        {
            int commandIndex = -1;
            for (int i = 0; i < _commandQueue.Count; i++)
            {
                var queuedCommand = _commandQueue[i];
                if (queuedCommand.Command.Id == commandId)
                {
                    var command = queuedCommand.Command;
                    if (command.State == CommandState.NotStarted ||
                        command.State == CommandState.InProgress)
                    {
                        commandIndex = i;
                        break;
                    }
                }
            }

            if (commandIndex > -1)
            {
                var command = _commandQueue[commandIndex].Command;
                command.CancellationToken.Cancel();
                _commandQueue.RemoveAt(commandIndex);

                return Result.Ok();
            }
        }

        return Result.Fail("Command not found");
    }

    public void StartExecutingCommands()
    {
        _ = StartCommandExecution();
    }

    private async Task StartCommandExecution()
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
            CommandBase? command = null;

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
                    command = _commandQueue[commandIndex].Command;
                    _commandQueue.RemoveAt(commandIndex);
                }
            }

            // Attempt to execute the command
            if (command is not null)
            {
                foreach (var executor in _executors)
                {
                    if (executor.CanExecuteCommand(command))
                    {
                        var executeResult = await executor.ExecuteCommand(command);
                        if (executeResult.IsFailure)
                        {
                            Log($"Command '{command}' failed: {executeResult.Error}");
                        }
                        break;
                    }
                }

                // Log the error and attempt to continue
                Log($"Command '{command}' failed because no registered executor could execute it.");
            }

            await Task.Delay(1);
        }
    }

    public void StopExecutingCommands()
    {
        _stopped = true;
    }

    private void Log(string message)
    {
        // Todo: Log to a file
        Console.WriteLine(message);
    }

}
