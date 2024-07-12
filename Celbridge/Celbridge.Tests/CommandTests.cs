using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Core;
using Celbridge.BaseLibrary.Logging;
using Celbridge.BaseLibrary.Messaging;
using Celbridge.Commands.Services;
using Celbridge.Logging.Services;
using Celbridge.Messaging.Services;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tests;

public class TestCommand : CommandBase
{
    private string _commandStackName = CommandStackNames.None;

    public override string StackName => _commandStackName;

    public bool ExecuteComplete { get; private set; }
    public bool UndoComplete { get; private set; }
    public bool RedoComplete { get; private set; }

    public TestCommand(string undoStackName)
    {
        _commandStackName = undoStackName;
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
    public const string CommandStackName = "TestCommandStack";

    private ServiceProvider? _serviceProvider;
    private ICommandService? _commandService;

    [SetUp]
    public void Setup()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IMessengerService, MessengerService>();
        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<ICommandService, CommandService>();

        _serviceProvider = services.BuildServiceProvider();
        _commandService = _serviceProvider.GetRequiredService<ICommandService>();

        _commandService.ActiveCommandStack = CommandStackName;

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

        var testCommand = new TestCommand(CommandStackNames.None);
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
        _commandService.IsUndoStackEmpty(CommandStackName).Should().BeTrue();

        var testCommand = new TestCommand(CommandStackName);
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
        _commandService.IsUndoStackEmpty(CommandStackName).Should().BeFalse();

        //
        // Undo the command
        //

        testCommand.UndoComplete.Should().BeFalse();
        
        var undoResult = _commandService.TryUndo();
        undoResult.IsSuccess.Should().BeTrue();
        undoResult.Value.Should().BeTrue();

        _commandService.IsUndoStackEmpty(CommandStackName).Should().BeTrue();

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

        _commandService.IsRedoStackEmpty(CommandStackName).Should().BeFalse();

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

        _commandService.IsUndoStackEmpty(CommandStackName).Should().BeFalse();
        _commandService.IsRedoStackEmpty(CommandStackName).Should().BeTrue();
    }
}
