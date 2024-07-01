using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Core;
using Celbridge.BaseLibrary.Logging;
using Celbridge.Commands.Services;
using Celbridge.Logging.Services;
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
    public const string TestStackName = "TestStack";

    private ServiceProvider? _serviceProvider;
    private ICommandService? _commandService;

    [SetUp]
    public void Setup()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<ICommandService, CommandService>();

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
        _commandService.IsUndoStackEmpty(TestStackName).Should().BeTrue();

        var testCommand = new TestCommand(TestStackName);
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
        _commandService.IsUndoStackEmpty(TestStackName).Should().BeFalse();

        //
        // Undo the command
        //

        testCommand.UndoComplete.Should().BeFalse();
        
        var undoResult = _commandService.Undo(TestStackName);
        undoResult.IsSuccess.Should().BeTrue();
        _commandService.IsUndoStackEmpty(TestStackName).Should().BeTrue();

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

        _commandService.IsRedoStackEmpty(TestStackName).Should().BeFalse();

        var redoResult = _commandService.Redo(TestStackName);
        redoResult.IsSuccess.Should().BeTrue();

        // Wait for redo to complete
        for (int i = 0; i < 10; i++)
        {
            if (testCommand.RedoComplete)
            {
                break;
            }
            await Task.Delay(50);
        }

        _commandService.IsUndoStackEmpty(TestStackName).Should().BeFalse();
        _commandService.IsRedoStackEmpty(TestStackName).Should().BeTrue();
    }
}
