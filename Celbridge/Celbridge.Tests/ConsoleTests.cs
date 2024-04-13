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
    public void ICanMoveThroughConsoleHistory()
    {
        var consoleHistory = new ConsoleHistory();

        consoleHistory.Add("A");
        consoleHistory.Add("B");

        var goBackResult1 = consoleHistory.CycleBackward();
        goBackResult1.IsSuccess.Should().BeTrue();
        goBackResult1.Value.Should().Be("B");

        var goBackResult2 = consoleHistory.CycleBackward();
        goBackResult2.IsSuccess.Should().BeTrue();
        goBackResult2.Value.Should().Be("A");

        var goForwardResult1 = consoleHistory.CycleForward();
        goForwardResult1.IsSuccess.Should().BeTrue();
        goForwardResult1.Value.Should().Be("B");
    }
}
