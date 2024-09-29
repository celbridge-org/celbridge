using Celbridge.Commands.Services;
using Celbridge.Commands;
using Celbridge.Core;
using Celbridge.Logging;
using Celbridge.Messaging.Services;
using Celbridge.Messaging;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Celbridge.Logging.Services;

namespace Celbridge.Tests;

public class TestCommand : CommandBase
{
    private UndoStackName _undoStackName = Commands.UndoStackName.None;

    public override UndoStackName UndoStackName => _undoStackName;

    public bool ExecuteComplete { get; private set; }
    public bool UndoComplete { get; private set; }
    public bool RedoComplete { get; private set; }

    public TestCommand(UndoStackName undoStackName)
    {
        _undoStackName = undoStackName;
    }

    public override async Task<Result> ExecuteAsync()
    {
        await Task.CompletedTask;

        ExecuteComplete = true;

        if (UndoComplete)
        {
            // Assume second time executing is a redo
            RedoComplete = true;
        }

        return Result.Ok();
    }

    public override async Task<Result> UndoAsync()
    {
        await Task.CompletedTask;

        UndoComplete = true;

        return Result.Ok();
    }
}

[TestFixture]
public class CommandTests
{
    public const UndoStackName UndoStack = UndoStackName.Explorer;

    private ServiceProvider? _serviceProvider;
    private ICommandService? _commandService;

    [SetUp]
    public void Setup()
    {
        var services = new ServiceCollection();

        Logging.ServiceConfiguration.ConfigureServices(services);
        services.AddSingleton<ILogSerializer, LogSerializer>();
        services.AddSingleton<IMessengerService, MessengerService>();
        services.AddSingleton<ICommandService, CommandService>();
        services.AddSingleton<IWorkspaceWrapper, WorkspaceWrapper>();

        _serviceProvider = services.BuildServiceProvider();
        _commandService = _serviceProvider.GetRequiredService<ICommandService>();

        _commandService.ActiveUndoStack = UndoStack;

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

        var testCommand = new TestCommand(Commands.UndoStackName.None);
        _commandService.EnqueueCommand(testCommand);

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
    public async Task ICanUndoAndRedoACommand()
    {
        Guard.IsNotNull(_commandService);

        //
        // Execute test command, using an undo stack
        //

        // The undo stack is currently empty
        _commandService.IsUndoStackEmpty(UndoStack).Should().BeTrue();

        var testCommand = new TestCommand(UndoStack);
        _commandService.EnqueueCommand(testCommand);

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

        // Undo stack should contain one item
        _commandService.IsUndoStackEmpty(UndoStack).Should().BeFalse();

        //
        // Undo the command
        //

        testCommand.UndoComplete.Should().BeFalse();
        
        var undoResult = _commandService.TryUndo();
        undoResult.IsSuccess.Should().BeTrue();
        undoResult.Value.Should().BeTrue();

        _commandService.IsUndoStackEmpty(UndoStack).Should().BeTrue();

        // Wait for undo to complete
        for (int i = 0; i < 10; i++)
        {
            if (testCommand.UndoComplete)
            {
                break;
            }
            await Task.Delay(50);
        }

        testCommand.UndoComplete.Should().BeTrue();

        //
        // Redo the command
        //

        _commandService.IsRedoStackEmpty(UndoStack).Should().BeFalse();

        var redoResult = _commandService.TryRedo();
        redoResult.IsSuccess.Should().BeTrue();
        redoResult.Value.Should().BeTrue();

        // Wait for redo to complete
        for (int i = 0; i < 10; i++)
        {
            if (testCommand.RedoComplete)
            {
                break;
            }
            await Task.Delay(50);
        }

        _commandService.IsUndoStackEmpty(UndoStack).Should().BeFalse();
        _commandService.IsRedoStackEmpty(UndoStack).Should().BeTrue();
    }
}
