using Celbridge.Commands.Services;
using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Logging.Services;
using Celbridge.Messaging.Services;
using Celbridge.Messaging;
using Celbridge.Settings;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tests.Commands;

public class TestCommand : CommandBase
{
    public bool ExecuteComplete { get; private set; }

    public override async Task<Result> ExecuteAsync()
    {
        await Task.CompletedTask;

        ExecuteComplete = true;

        return Result.Ok();
    }
}

public class ThrowingTestCommand : CommandBase
{
    public override Task<Result> ExecuteAsync()
    {
        throw new InvalidOperationException("Simulated command failure for test");
    }
}

public class SuppressLogTestCommand : CommandBase
{
    public override CommandFlags CommandFlags => CommandFlags.SuppressCommandLog;

    public bool ExecuteComplete { get; private set; }

    public override Task<Result> ExecuteAsync()
    {
        ExecuteComplete = true;
        return Task.FromResult(Result.Ok());
    }
}

/// <summary>
/// A command that throws from ExecuteAsync AND whose OnExecute callback itself throws.
/// Used to verify the inner fallback catch block in the command worker loop: even when
/// the failure-notification callback throws, the worker must keep running and continue
/// processing subsequent commands.
/// </summary>
public class DoubleThrowingTestCommand : CommandBase
{
    public DoubleThrowingTestCommand()
    {
        OnExecute = _ => throw new InvalidOperationException("Simulated callback failure for test");
    }

    public override Task<Result> ExecuteAsync()
    {
        throw new InvalidOperationException("Simulated command failure for test");
    }
}

[TestFixture]
public class CommandTests
{
    private ServiceProvider? _serviceProvider;
    private ICommandService? _commandService;
    private ISettingsService? _settingsService;
    private IDialogService? _dialogService;

    [SetUp]
    public void Setup()
    {
        var services = new ServiceCollection();

        Logging.ServiceConfiguration.ConfigureServices(services);
        services.AddSingleton<ILogSerializer, LogSerializer>();
        services.AddSingleton<IMessengerService, MessengerService>();
        services.AddSingleton<ICommandService, CommandService>();
        services.AddSingleton<IWorkspaceWrapper, WorkspaceWrapper>();

        // The command loop flushes application settings each tick; a substitute is
        // enough since these tests do not exercise settings persistence.
        _settingsService = Substitute.For<ISettingsService>();
        _settingsService.FlushAsync().Returns(Task.FromResult(Result.Ok()));
        services.AddSingleton(_settingsService);

        // The command loop holds its dequeue while a dialog is open.
        _dialogService = Substitute.For<IDialogService>();
        _dialogService.IsDialogOpen.Returns(false);
        services.AddSingleton(_dialogService);
        services.AddTransient<TestCommand>();
        services.AddTransient<ThrowingTestCommand>();
        services.AddTransient<SuppressLogTestCommand>();
        services.AddTransient<DoubleThrowingTestCommand>();

        _serviceProvider = services.BuildServiceProvider();
        _commandService = _serviceProvider.GetRequiredService<ICommandService>();

        var commandService = _commandService as CommandService;
        Guard.IsNotNull(commandService);
        commandService.StartExecution();
    }

    [TearDown]
    public void TearDown()
    {
        var commandService = _commandService as CommandService;
        Guard.IsNotNull(commandService);
        commandService.StopExecution();

        if (_serviceProvider != null)
        {
            (_serviceProvider as IDisposable)?.Dispose();
        }
    }

    [Test]
    public async Task ICanExecuteACommand()
    {
        Guard.IsNotNull(_commandService);

        TestCommand? testCommand = null;
        _commandService.Execute<TestCommand>(command =>
        {
            testCommand = command;
        });
        Guard.IsNotNull(testCommand);

        // Wait for command to execute
        for (int i = 0; i < 10; i++)
        {
            if (testCommand.ExecuteComplete)
            {
                break;
            }
            await Task.Delay(50);
        }

        testCommand.ExecuteComplete.Should().BeTrue();
    }

    [Test]
    public async Task ExecuteAsync_CompletesEvenWhenCommandThrows()
    {
        // Regression test: if a command throws during ExecuteAsync the command service must still
        // complete the awaiting task rather than leaving the caller (e.g. an MCP tool handler)
        // blocked forever. Pre-fix, ExecuteAsync<T> would deadlock because the TaskCompletionSource
        // was never signalled.
        Guard.IsNotNull(_commandService);

        // Cap the wait at five seconds: if the fix regresses, the await hangs indefinitely.
        var executionTask = _commandService.ExecuteAsync<ThrowingTestCommand>();
        var completedTask = await Task.WhenAny(executionTask, Task.Delay(TimeSpan.FromSeconds(5)));

        completedTask.Should().BeSameAs(executionTask, "ExecuteAsync must not hang when the command throws");

        var result = await executionTask;
        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task Execute_FireAndForgetCommandThatThrows_DoesNotStopWorker()
    {
        // When a command is enqueued via fire-and-forget Execute<T>, command.OnExecute is null so
        // the failure-notification branch at line 281 no-ops. This test verifies the outer catch
        // still prevents the worker loop from dying: a subsequent normal command must still run.
        Guard.IsNotNull(_commandService);

        var enqueueResult = _commandService.Execute<ThrowingTestCommand>();
        enqueueResult.IsSuccess.Should().BeTrue();

        TestCommand? followUpCommand = null;
        _commandService.Execute<TestCommand>(command =>
        {
            followUpCommand = command;
        });
        Guard.IsNotNull(followUpCommand);

        // Wait for the follow-up command to run, proving the worker survived the preceding throw.
        for (int i = 0; i < 20; i++)
        {
            if (followUpCommand.ExecuteComplete)
            {
                break;
            }
            await Task.Delay(50);
        }

        followUpCommand.ExecuteComplete.Should().BeTrue("the worker loop must continue after an unawaited throwing command");
    }

    [Test]
    public async Task Execute_CallbackThatThrows_IsSwallowedByInnerCatch()
    {
        // If the OnExecute failure-notification itself throws (line 281), the inner catch at
        // line 283 must swallow the callback exception so the worker loop keeps running.
        // We observe the fix indirectly: enqueue a double-throwing command, then a normal command,
        // and assert the normal command completes.
        Guard.IsNotNull(_commandService);

        var enqueueResult = _commandService.Execute<DoubleThrowingTestCommand>();
        enqueueResult.IsSuccess.Should().BeTrue();

        TestCommand? followUpCommand = null;
        _commandService.Execute<TestCommand>(command =>
        {
            followUpCommand = command;
        });
        Guard.IsNotNull(followUpCommand);

        for (int i = 0; i < 20; i++)
        {
            if (followUpCommand.ExecuteComplete)
            {
                break;
            }
            await Task.Delay(50);
        }

        followUpCommand.ExecuteComplete.Should().BeTrue("the worker loop must continue even when OnExecute itself throws");
    }

    [Test]
    public async Task SuppressCommandLog_FlaggedCommandExecutes()
    {
        // Commands flagged with SuppressCommandLog are routed through the same queue as
        // mutating commands. They should still execute end-to-end; the only difference is
        // that the per-command audit log entry is skipped.
        Guard.IsNotNull(_commandService);

        SuppressLogTestCommand? capturedCommand = null;
        await _commandService.ExecuteAsync<SuppressLogTestCommand>(command =>
        {
            capturedCommand = command;
        });

        Guard.IsNotNull(capturedCommand);
        capturedCommand.ExecuteComplete.Should().BeTrue();
        capturedCommand.CommandFlags.HasFlag(CommandFlags.SuppressCommandLog).Should().BeTrue();
    }

    [Test]
    public async Task Execute_QueuedCommandWaitsUntilTheDialogCloses()
    {
        Guard.IsNotNull(_commandService);
        Guard.IsNotNull(_dialogService);

        _dialogService.IsDialogOpen.Returns(true);

        TestCommand? queuedCommand = null;
        _commandService.Execute<TestCommand>(command =>
        {
            queuedCommand = command;
        });
        Guard.IsNotNull(queuedCommand);

        // Long enough for the loop to tick many times over the held queue.
        await Task.Delay(300);

        queuedCommand.ExecuteComplete.Should().BeFalse("a queued command must not run while a dialog is open");

        _dialogService.IsDialogOpen.Returns(false);

        for (int i = 0; i < 20; i++)
        {
            if (queuedCommand.ExecuteComplete)
            {
                break;
            }
            await Task.Delay(50);
        }

        queuedCommand.ExecuteComplete.Should().BeTrue("closing the dialog must release the queue");
    }

    [Test]
    public async Task ExecuteImmediate_RunsWhileADialogIsOpen()
    {
        // The settings dialog runs its own commands this way. Enqueuing them under the gate would
        // leave them pending until the dialog closed, and deadlock the ones that are awaited.
        Guard.IsNotNull(_commandService);
        Guard.IsNotNull(_dialogService);

        _dialogService.IsDialogOpen.Returns(true);

        TestCommand? immediateCommand = null;
        var executionTask = _commandService.ExecuteImmediate<TestCommand>(command =>
        {
            immediateCommand = command;
        });

        var completedTask = await Task.WhenAny(executionTask, Task.Delay(TimeSpan.FromSeconds(5)));
        completedTask.Should().BeSameAs(executionTask, "ExecuteImmediate must not wait on the queue gate");

        Guard.IsNotNull(immediateCommand);
        immediateCommand.ExecuteComplete.Should().BeTrue();
    }

    [Test]
    public async Task UpdateApplication_KeepsTickingWhileADialogIsOpen()
    {
        // Only the dequeue is gated. The application update carries on so deferred settings writes and
        // document auto-saves still reach disk while the dialog is up.
        Guard.IsNotNull(_dialogService);
        Guard.IsNotNull(_settingsService);

        _dialogService.IsDialogOpen.Returns(true);
        _settingsService.ClearReceivedCalls();

        await Task.Delay(300);

        await _settingsService.ReceivedWithAnyArgs().FlushAsync();
    }
}
