using Celbridge.Console.Services;
using Celbridge.Console;
using Celbridge.Logging.Services;
using Celbridge.Logging;
using Celbridge.Messaging.Services;
using Celbridge.Messaging;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tests;

[TestFixture]
public class ConsoleTests
{
    private ServiceProvider? _serviceProvider;

    [SetUp]
    public void Setup()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IMessengerService, MessengerService>();
        services.AddSingleton<IConsoleService, ConsoleService>();
        services.AddSingleton<ILogger<CommandTests>, Logger<CommandTests>>();
        services.AddTransient<CommandHistory>();

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
    public void ICanSelectNextAndPreviousCommandHistory()
    {
        Guard.IsNotNull(_serviceProvider);

        var commandHistory = _serviceProvider.GetRequiredService<CommandHistory>();

        // Add command "A" to the history
        commandHistory.AddCommand("A");
        commandHistory.CanSelectPreviousCommand.Should().BeTrue();
        commandHistory.CanSelectNextCommand.Should().BeFalse();

        // Add command "B" to the history
        commandHistory.AddCommand("B");
        commandHistory.CanSelectPreviousCommand.Should().BeTrue();
        commandHistory.CanSelectNextCommand.Should().BeFalse();

        commandHistory.NumCommands.Should().Be(2);

        // Current command should be the empty string
        commandHistory.GetSelectedCommand().Value.Should().Be(string.Empty);
        commandHistory.CanSelectNextCommand.Should().BeFalse();
        commandHistory.CanSelectPreviousCommand.Should().BeTrue();

        // Select previous command "B"
        commandHistory.SelectPreviousCommand().IsSuccess.Should().BeTrue();
        commandHistory.GetSelectedCommand().Value.Should().Be("B");
        commandHistory.CanSelectNextCommand.Should().BeTrue();
        commandHistory.CanSelectPreviousCommand.Should().BeTrue();

        // Select previous command "A"
        commandHistory.SelectPreviousCommand().IsSuccess.Should().BeTrue();
        commandHistory.GetSelectedCommand().Value.Should().Be("A");
        commandHistory.CanSelectNextCommand.Should().BeTrue();
        commandHistory.CanSelectPreviousCommand.Should().BeFalse();
        commandHistory.SelectPreviousCommand().IsFailure.Should().BeTrue();

        // Select next command "B"
        commandHistory.SelectNextCommand().IsSuccess.Should().BeTrue();
        commandHistory.GetSelectedCommand().Value.Should().Be("B");
        commandHistory.CanSelectNextCommand.Should().BeTrue();
        commandHistory.CanSelectPreviousCommand.Should().BeTrue();

        // Select next command <empty>
        commandHistory.SelectNextCommand().IsSuccess.Should().BeTrue();
        commandHistory.GetSelectedCommand().Value.Should().Be(string.Empty);
        commandHistory.CanSelectNextCommand.Should().BeFalse();
        commandHistory.CanSelectPreviousCommand.Should().BeTrue();
        commandHistory.SelectNextCommand().IsFailure.Should().BeTrue();
    }
}
