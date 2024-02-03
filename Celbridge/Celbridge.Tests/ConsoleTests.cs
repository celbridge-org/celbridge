using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.Logging;
using Celbridge.CommonServices.Logging;
using Celbridge.CoreExtensions.Console;
using CommunityToolkit.Mvvm.Messaging;
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

        // Mimic your application's service registration
        // For example, register a real service
        services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
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
        var loggingService = _serviceProvider!.GetRequiredService<IConsoleService>();

        var result = await loggingService.Execute("print");

        result.IsSuccess.Should().BeTrue();
    }
}
