using Celbridge.BaseLibrary.Logging;
using System.Diagnostics;

namespace Celbridge.Services.Commands;

public class CommandService : ICommandService
{
    private readonly ILoggingService _loggingService;

    // ExecutionTime is the time in milliseconds when the command should be executed
    private record QueuedCommand(ICommand Command, long ExecutionTime);

    private readonly List<QueuedCommand> _commandQueue = new();

    private object _lock = new object();

    private readonly Stopwatch _stopwatch = new();

    private bool _stopped = false;

    public CommandService(ILoggingService loggingService)
    {
        _loggingService = loggingService;
    }

    public Result Execute<T>(Action<T> configure) where T : ICommand
    {
        return Execute<T>(configure, 0);
    }

    public Result Execute<T>(Action<T> configure, uint delay) where T : ICommand
    {
        var command = CreateCommand<T>();
        configure.Invoke(command);
        return EnqueueCommand(command, delay);
    }

    public Result Execute<T>() where T : ICommand
    {
        return Execute<T>(0);
    }

    public Result Execute<T>(uint delay) where T : ICommand
    {
        var command = CreateCommand<T>();
        return EnqueueCommand(command, delay);
    }

    public T CreateCommand<T>() where T : ICommand
    {
        var serviceProvider = ServiceLocator.ServiceProvider;
        T command = serviceProvider.GetRequiredService<T>();

        return command;
    }

    public Result EnqueueCommand(ICommand command)
    {
        return EnqueueCommand(command, 0);
    }

    public Result EnqueueCommand(ICommand command, uint delay)
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

    public void RemoveCommandsOfType<T>() where T : notnull
    {
        lock (_lock)
        {
            _commandQueue.RemoveAll(c => c.GetType().IsAssignableTo(typeof(T)) );
        }
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
            ICommand? command = null;

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

            if (command is not null)
            {
                try
                {
                    // Attempt to execute the command
                    var executeResult = await command.ExecuteAsync();
                    if (executeResult.IsFailure)
                    {
                        _loggingService.Error($"Command '{command}' failed: {executeResult.Error}");
                    }
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
