using System.Diagnostics;
using System.Runtime.CompilerServices;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Settings;
using Celbridge.Workspace;

namespace Celbridge.Commands.Services;

using ICommandLogger = ILogger<CommandService>;

public class CommandService : ICommandService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICommandLogger _logger;
    private readonly ILogSerializer _logSerializer;
    private readonly IMessengerService _messengerService;
    private readonly ISettingsService _settingsService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    private record QueuedCommand(IExecutableCommand Command);

    private readonly List<QueuedCommand> _commandQueue = new();

    private object _lock = new object();

    private readonly Stopwatch _stopwatch = new();
    private double _lastWorkspaceUpdateTime = 0;

    private bool _stopped = false;

    private static readonly TimeSpan WatchdogWarningInterval = TimeSpan.FromSeconds(5);

    public CommandService(
        IServiceProvider serviceProvider,
        ICommandLogger logger,
        ILogSerializer logSerializer,
        IMessengerService messengerService,
        ISettingsService settingsService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _logSerializer = logSerializer;
        _messengerService = messengerService;
        _settingsService = settingsService;
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

    public async Task<Result<TResult>> ExecuteImmediate<TCommand, TResult>(
        Action<TCommand>? configure = null,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
        where TCommand : IExecutableCommand<TResult>
        where TResult : notnull
    {
        // ExecuteImmediate<TCommand> resolves the command from DI internally, so we capture a
        // reference to it via the configure callback to read ResultValue after execution completes.
        TCommand? capturedCommand = default;

        var result = await ExecuteImmediate<TCommand>(command =>
        {
            configure?.Invoke(command);
            capturedCommand = command;
        }, filePath, lineNumber);

        if (result.IsFailure)
        {
            return Result.Fail(result);
        }

        return Result<TResult>.Ok(capturedCommand!.ResultValue);
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
            return Result.Fail(executionResult);
        }

        return Result.Ok();
    }

    public async Task<Result<TResult>> ExecuteAsync<TCommand, TResult>(
        Action<TCommand>? configure = null,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
        where TCommand : IExecutableCommand<TResult>
        where TResult : notnull
    {
        // ExecuteAsync<TCommand> resolves the command from DI internally, so we
        // don't have direct access to the command instance. We capture a reference
        // to it via the configure callback so we can read ResultValue after
        // execution completes.
        TCommand? capturedCommand = default;

        var result = await ExecuteAsync<TCommand>(command =>
        {
            configure?.Invoke(command);
            capturedCommand = command;
        }, filePath, lineNumber);

        if (result.IsFailure)
        {
            return Result.Fail(result);
        }

        // The command populated ResultValue during its ExecuteAsync().
        return Result<TResult>.Ok(capturedCommand!.ResultValue);
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

            // To avoid race conditions, application and workspace state is updated while there are no
            // executing commands. This ensures that no commands run until pending saves complete.
            var updateResult = await ExecuteWithWatchdogAsync(UpdateApplicationAsync(), "Application update");
            if (updateResult.IsFailure)
            {
                _logger.LogError(updateResult, "Failed to update application");
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
                        // Log the command execution at the debug level. Commands tagged with
                        // SuppressCommandLog (typically read-only queries polled by tools) are
                        // skipped to avoid flooding the audit log.
                        bool suppressLog = command.CommandFlags.HasFlag(CommandFlags.SuppressCommandLog);
                        if (!suppressLog)
                        {
                            string logEntry = _logSerializer.SerializeObject(startedMessage, false);
                            _logger.LogDebug(logEntry);
                        }

                        var executeResult = await ExecuteWithWatchdogAsync(command.ExecuteAsync(), $"Command '{command.GetType().Name}'");

                        if (executeResult.IsFailure)
                        {
                            _logger.LogError(executeResult, "Execute command failed");
                        }

                        // Update the resource registry synchronously before notifying callers.
                        // This ensures ExecuteAsync callers see an up-to-date registry.
                        if (command.CommandFlags.HasFlag(CommandFlags.UpdateResources))
                        {
                            var message = new RequestResourceRegistryUpdateMessage();
                            _messengerService.Send(message);
                        }

                        // Refresh the resource tree view if the command requires it.
                        // This is a lightweight alternative to UpdateResources for commands
                        // that only modify tree view state without changing resources on disk.
                        // Skip if UpdateResources is also set, as the registry update already
                        // triggers a tree rebuild.
                        if (command.CommandFlags.HasFlag(CommandFlags.RefreshResourceTree) &&
                            !command.CommandFlags.HasFlag(CommandFlags.UpdateResources))
                        {
                            var refreshMessage = new RefreshResourceTreeMessage();
                            _messengerService.Send(refreshMessage);
                        }

                        // Call the OnExecute callback if it is set.
                        // This is used by the ExecuteAsync() methods to notify the caller about the execution.
                        command.OnExecute?.Invoke(executeResult);

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
                    _logger.LogError(ex, $"An exception occurred when executing the command. Check the log file for more information.");

                    // Complete the OnExecute callback with a failure so any awaiting ExecuteAsync call
                    // returns instead of hanging forever.
                    try
                    {
                        var failureResult = Result.Fail($"Command execution threw an exception: {ex.Message}")
                            .WithException(ex);
                        command.OnExecute?.Invoke(failureResult);
                    }
                    catch (Exception callbackException)
                    {
                        _logger.LogError(callbackException, "An exception occurred while notifying command callback of a failure.");
                    }
                }
            }

            await Task.Delay(1);
        }
    }

    // Awaits a command-loop operation while a watchdog logs a warning if it runs unusually long.
    // The serial queue cannot advance until the awaited operation returns, so a genuine hang would
    // otherwise leave no trace. The operation is never cancelled; the watchdog only emits a warning,
    // and repeats it while the operation stays blocked so a permanent hang keeps reporting itself.
    private async Task<Result> ExecuteWithWatchdogAsync(Task<Result> operation, string operationName)
    {
        using var watchdogCancellation = new CancellationTokenSource();
        var startElapsed = _stopwatch.Elapsed;

        while (true)
        {
            var warningDelay = Task.Delay(WatchdogWarningInterval, watchdogCancellation.Token);
            var completedTask = await Task.WhenAny(operation, warningDelay);

            if (ReferenceEquals(completedTask, operation))
            {
                watchdogCancellation.Cancel();
                return await operation;
            }

            var blockedSeconds = (_stopwatch.Elapsed - startElapsed).TotalSeconds;
            _logger.LogWarning(
                $"Command queue blocked: {operationName} has run for {blockedSeconds:F0}s without completing. " +
                $"No further commands will execute until it returns.");
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
    /// Flushes pending saves between commands, so a save never races a command.
    /// </summary>
    private async Task<Result> UpdateApplicationAsync()
    {
        bool failed = false;

        // Application-scope settings (theme, window geometry, Workshop config) are
        // written on the UI thread but deferred, so the disk write happens here, off
        // the UI thread. FlushAsync is a no-op when nothing changed since the last tick.
        var flushResult = await _settingsService.FlushAsync();
        if (flushResult.IsFailure)
        {
            failed = true;
            _logger.LogError(flushResult, "Failed to flush application settings");
        }

        // Updating the workspace systems is a subset of the application update that
        // runs only when a workspace is loaded.
        var now = _stopwatch.Elapsed.TotalSeconds;
        if (_workspaceWrapper.IsWorkspacePageLoaded &&
            _lastWorkspaceUpdateTime != 0)
        {
            var deltaTime = now - _lastWorkspaceUpdateTime;

            var updateResult = await _workspaceWrapper.WorkspaceService.UpdateWorkspaceAsync(deltaTime);
            if (updateResult.IsFailure)
            {
                // The workspace service logs the specific failures it encountered.
                failed = true;
            }

            // Use the latest reported time to account for any time spent saving.
            _lastWorkspaceUpdateTime = _stopwatch.Elapsed.TotalSeconds;
        }
        else
        {
            // No workspace is loaded, or this is the first call so we can't calculate a delta time.
            _lastWorkspaceUpdateTime = now;
        }

        if (failed)
        {
            return Result.Fail("Failed to update application");
        }

        return Result.Ok();
    }
}
