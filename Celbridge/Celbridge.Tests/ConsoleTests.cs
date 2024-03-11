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
    public async Task TestPrintCommand()
    {
        var consoleService = _serviceProvider!.GetRequiredService<IConsoleService>();

        var result = await consoleService.Execute("print");

        result.IsSuccess.Should().BeTrue();
    }
}
