using Celbridge.Commands;
using Celbridge.Commands.Services;
using Celbridge.Dialog;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Settings;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tests.Commands;

/// <summary>
/// A command that runs until the test releases it.
/// </summary>
public class BlockingTestCommand : CommandBase
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async Task<Result> ExecuteAsync()
    {
        Started.SetResult();
        await Released.Task;

        return Result.Ok();
    }
}

/// <summary>
/// Covers the command queue's watchdog, which warns about a command that runs long. A command that waits on a
/// dialog is waiting for the user, so the watchdog counts only the time after the dialog closes.
/// </summary>
[TestFixture]
public class CommandWatchdogTests
{
    private static readonly TimeSpan WarningInterval = TimeSpan.FromMilliseconds(200);

    private ServiceProvider _serviceProvider = null!;
    private ILogger<CommandService> _logger = null!;
    private IMessengerService _messengerService = null!;
    private IDialogService _dialogService = null!;
    private CommandService _commandService = null!;

    [SetUp]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddTransient<BlockingTestCommand>();
        _serviceProvider = services.BuildServiceProvider();

        _logger = Substitute.For<ILogger<CommandService>>();
        _messengerService = new MessengerService();

        var settingsService = Substitute.For<ISettingsService>();
        settingsService.FlushAsync().Returns(Task.FromResult(Result.Ok()));

        _dialogService = Substitute.For<IDialogService>();
        _dialogService.IsDialogOpen.Returns(false);

        _commandService = new CommandService(
            _serviceProvider,
            _logger,
            Substitute.For<ILogSerializer>(),
            _messengerService,
            settingsService,
            Substitute.For<IWorkspaceWrapper>(),
            _dialogService)
        {
            WatchdogWarningInterval = WarningInterval
        };

        _commandService.StartExecution(dispatcherQueue: null);
    }

    [TearDown]
    public void TearDown()
    {
        _commandService.StopExecution();
        _serviceProvider.Dispose();
    }

    [Test]
    public async Task ACommandThatRunsLong_IsReported()
    {
        var command = await StartBlockingCommandAsync();

        await Task.Delay(WarningInterval * 3);
        command.Released.SetResult();

        _logger.Received().LogWarning(Arg.Is<string?>(message => IsBlockedWarning(message)), Arg.Any<object?[]>());
    }

    [Test]
    public async Task ACommandWaitingOnADialog_IsNotReported()
    {
        var command = await StartBlockingCommandAsync();

        // The command shows a dialog, and the user answers it after several warning intervals. The command finishes
        // as soon as the dialog closes.
        _dialogService.IsDialogOpen.Returns(true);
        await Task.Delay(WarningInterval * 3);
        CloseTheDialog();
        command.Released.SetResult();

        _logger.DidNotReceive().LogWarning(Arg.Is<string?>(message => IsBlockedWarning(message)), Arg.Any<object?[]>());
    }

    [Test]
    public async Task ACommandStillRunningAfterTheDialogCloses_IsReported()
    {
        var command = await StartBlockingCommandAsync();

        _dialogService.IsDialogOpen.Returns(true);
        await Task.Delay(WarningInterval * 3);
        CloseTheDialog();
        await Task.Delay(WarningInterval * 3);
        command.Released.SetResult();

        _logger.Received().LogWarning(Arg.Is<string?>(message => IsBlockedWarning(message)), Arg.Any<object?[]>());
    }

    private async Task<BlockingTestCommand> StartBlockingCommandAsync()
    {
        BlockingTestCommand? blockingCommand = null;
        _commandService.Execute<BlockingTestCommand>(command => blockingCommand = command);
        Guard.IsNotNull(blockingCommand);

        await blockingCommand.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        return blockingCommand;
    }

    // The close time is recorded before IsDialogOpen turns false. So a watchdog tick between the two steps never
    // counts the time the dialog was open.
    private void CloseTheDialog()
    {
        _messengerService.Send(new ModalDialogClosedMessage());
        _dialogService.IsDialogOpen.Returns(false);
    }

    private static bool IsBlockedWarning(string? message)
    {
        return message is not null
            && message.StartsWith("Command queue blocked");
    }
}
