using Celbridge.Commands;
using Celbridge.Core;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Commands.Services;
using Celbridge.Logging.Services;
using Celbridge.Messaging.Services;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Celbridge.Workspace;
using Celbridge.UserInterface.Services;

namespace Celbridge.Tests;

public class TestCommand : CommandBase
{
    private string _undoStackName = UndoStackNames.None;

    public override string UndoStackName => _undoStackName;

    public bool ExecuteComplete { get; private set; }
    public bool UndoComplete { get; private set; }
    public bool RedoComplete { get; private set; }

    public TestCommand(string undoStackName)
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
    public const string UndoStackName = "TestUndoStack";

    private ServiceProvider? _serviceProvider;
    private ICommandService? _commandService;

    [SetUp]
    public void Setup()
    {
        var services = new ServiceCollection();

        Logging.ServiceConfiguration.ConfigureServices(services);
        services.AddSingleton<IMessengerService, MessengerService>();
        services.AddSingleton<ICommandService, CommandService>();
        services.AddSingleton<IWorkspaceWrapper, WorkspaceWrapper>();

        _serviceProvider = services.BuildServiceProvider();
        _commandService = _serviceProvider.GetRequiredService<ICommandService>();

        _commandService.ActiveUndoStack = UndoStackName;

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

        var testCommand = new TestCommand(UndoStackNames.None);
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
        _commandService.IsUndoStackEmpty(UndoStackName).Should().BeTrue();

        var testCommand = new TestCommand(UndoStackName);
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
        _commandService.IsUndoStackEmpty(UndoStackName).Should().BeFalse();

        //
        // Undo the command
        //

        testCommand.UndoComplete.Should().BeFalse();
        
        var undoResult = _commandService.TryUndo();
        undoResult.IsSuccess.Should().BeTrue();
        undoResult.Value.Should().BeTrue();

        _commandService.IsUndoStackEmpty(UndoStackName).Should().BeTrue();

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

        _commandService.IsRedoStackEmpty(UndoStackName).Should().BeFalse();

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

        _commandService.IsUndoStackEmpty(UndoStackName).Should().BeFalse();
        _commandService.IsRedoStackEmpty(UndoStackName).Should().BeTrue();
    }
}
