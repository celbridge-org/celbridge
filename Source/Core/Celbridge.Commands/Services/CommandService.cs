using System.Diagnostics;
using System.Runtime.CompilerServices;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Workspace;

namespace Celbridge.Commands.Services;

using ICommandLogger = ILogger<CommandService>;

public class CommandService : ICommandService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICommandLogger _logger;
    private readonly ILogSerializer _logSerializer;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    private record QueuedCommand(IExecutableCommand Command);

    private readonly List<QueuedCommand> _commandQueue = new();

    private object _lock = new object();

    private readonly Stopwatch _stopwatch = new();
    private double _lastWorkspaceUpdateTime = 0;

    private bool _stopped = false;

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

        return EnqueueCommand(command);
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

        var enqueueResult = EnqueueCommand(command);
        if (enqueueResult.IsFailure)
        {
            return Result.Fail($"Failed to enqueue command")
                .WithErrors(enqueueResult);
        }

        // Wait for the command to execute
        await tcs.Task;

        // Clear the callback
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

            lock (_lock)
            {
                if (_commandQueue.Count > 0)
                {
                    var item = _commandQueue[0];
                    command = item.Command;
                    _commandQueue.RemoveAt(0);
                }
            }

            if (command is not null)
            {
                try
                {
                    // Notify listeners that the command is about to execute
                    var startedMessage = new ExecuteCommandStartedMessage(command, (float)_stopwatch.Elapsed.TotalSeconds);
                    _messengerService.Send(startedMessage);

                    var scopeName = $"Execute {command.GetType().Name}";
                    using (_logger.BeginScope(scopeName))
                    {
                        // Log the command execution at the debug level
                        string logEntry = _logSerializer.SerializeObject(startedMessage, false);
                        _logger.LogDebug(logEntry);

                        var executeResult = await command.ExecuteAsync();

                        if (executeResult.IsFailure)
                        {
                            _logger.LogError(executeResult, "Execute command failed");
                        }

                        // Call the OnExecute callback if it is set.
                        // This is used by the ExecuteAsync() methods to notify the caller about the execution.
                        command.OnExecute?.Invoke(executeResult);

                        // Handle resource updates based on command flags
                        if (command.CommandFlags.HasFlag(CommandFlags.ForceUpdateResources))
                        {
                            var message = new RequestResourceRegistryUpdateMessage(ForceImmediate: true);
                            _messengerService.Send(message);
                        }
                        else if (command.CommandFlags.HasFlag(CommandFlags.RequestUpdateResources))
                        {
                            var message = new RequestResourceRegistryUpdateMessage(ForceImmediate: false);
                            _messengerService.Send(message);
                        }

                        // Save the workspace state if the command requires it.
                        if (command.CommandFlags.HasFlag(CommandFlags.SaveWorkspaceState))
                        {
                            var message = new WorkspaceStateDirtyMessage();
                            _messengerService.Send(message);
                        }

                        var endedMessage = new ExecuteCommandEndedMessage(command, (float)_stopwatch.Elapsed.TotalSeconds);
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

    private Result EnqueueCommand(IExecutableCommand command)
    {
        lock (_lock)
        {
            if (_commandQueue.Any((item) => item.Command.CommandId == command.CommandId))
            {
                return Result.Fail($"Command '{command.CommandId}' is already in the execution queue");
            }

            _commandQueue.Add(new QueuedCommand(command));
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
}
