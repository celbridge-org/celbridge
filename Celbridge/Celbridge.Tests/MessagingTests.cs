using Celbridge.BaseLibrary.Logging;
using Celbridge.BaseLibrary.Messaging;
using Celbridge.CommonServices.Logging;
using Celbridge.CommonServices.Messaging;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tests;

[TestFixture]
public class MessagingTests
{
    private ServiceProvider? _serviceProvider;

    [SetUp]
    public void Setup()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IMessengerService, MessengerService>();
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

    public record TestMessage;

    [Test]
    public void TestSendMessage()
    {
        var messengerService = _serviceProvider!.GetRequiredService<IMessengerService>();
        var loggingService = _serviceProvider!.GetRequiredService<ILoggingService>();

        bool received = false;
        messengerService.Register<TestMessage>(this, (r, m) =>
        {
            loggingService.Info($"Got the message: {m}");
            received = true;
        });

        var message = new TestMessage();
        messengerService.Send(message);

        received.Should().BeTrue();
    }
}
