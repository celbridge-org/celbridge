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
    public bool Completed { get; private set; }

    public override async Task<Result> ExecuteAsync()
    {
        await Task.Delay(1);

        Completed = true;

        return Result.Ok();
    }
}

[TestFixture]
public class CommandTests
{
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

        var testCommand = new TestCommand();
        _commandService.EnqueueCommand(testCommand);

        // Wait for command to execute
        for (int i = 0; i < 10; i++)
        {
            if (testCommand.Completed)
            {
                break;
            }
            await Task.Delay(50);
        }

        testCommand.Completed.Should().BeTrue();
    }
}
