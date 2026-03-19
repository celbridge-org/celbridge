using Celbridge.Commands.Services;
using Celbridge.Commands;
using Celbridge.Logging.Services;
using Celbridge.Logging;
using Celbridge.Messaging.Services;
using Celbridge.Messaging;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tests;

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

[TestFixture]
public class CommandTests
{
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
        services.AddTransient<TestCommand>();

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
}
