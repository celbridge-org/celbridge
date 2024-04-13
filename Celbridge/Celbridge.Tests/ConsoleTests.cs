using Microsoft.Extensions.DependencyInjection;
using Celbridge.Console;
using Celbridge.Services.Messaging;
using Celbridge.Services.Logging;
using Celbridge.BaseLibrary.Messaging;
using Celbridge.BaseLibrary.Logging;
using Celbridge.BaseLibrary.Console;

namespace Celbridge.Tests;

[TestFixture]
public class ConsoleTests
{
    private ServiceProvider? _serviceProvider;

    [SetUp]
    public void Setup()
    {
        var services = new ServiceCollection();

        // Mimic your application's service registration
        // For example, register a real service
        services.AddSingleton<IMessengerService, MessengerService>();
        services.AddSingleton<IConsoleService, ConsoleService>();
        services.AddSingleton<ILoggingService, LoggingService>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        if (_serviceProvider != null)
        {
            (_serviceProvider as IDisposable)?.Dispose();
        }
    }

    [Test]
    public void ICanMoveThroughCommandHistory()
    {
        var commandHistory = new CommandHistory() as ICommandHistory;

        // Add 2 commands to the history
        commandHistory.AddCommand("A");
        commandHistory.AddCommand("B");

        // Check current command is "B"
        commandHistory.HistorySize.Should().Be(2);
        commandHistory.GetCurrentCommand().Value.Should().Be("B");
        commandHistory.CanMoveToNextCommand.Should().BeFalse();
        commandHistory.CanMoveToPreviousCommand.Should().BeTrue();

        // Move to the previous command "A"
        commandHistory.MoveToPreviousCommand().IsSuccess.Should().BeTrue();
        commandHistory.GetCurrentCommand().Value.Should().Be("A");
        commandHistory.CanMoveToNextCommand.Should().BeTrue();
        commandHistory.CanMoveToPreviousCommand.Should().BeFalse();

        // Move to the next command "B"
        commandHistory.MoveToNextCommand().IsSuccess.Should().BeTrue();
        commandHistory.GetCurrentCommand().Value.Should().Be("B");
        commandHistory.CanMoveToNextCommand.Should().BeFalse();
        commandHistory.CanMoveToPreviousCommand.Should().BeTrue();

        // Clear the command history
        commandHistory.Clear();
        commandHistory.HistorySize.Should().Be(0);
        commandHistory.CanMoveToNextCommand.Should().BeFalse();
        commandHistory.CanMoveToPreviousCommand.Should().BeFalse();
    }
}
