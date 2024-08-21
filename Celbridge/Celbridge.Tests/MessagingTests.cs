using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Logging.Services;
using Celbridge.Messaging.Services;
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
        services.AddSingleton<ILoggingService<MessagingTests>, LoggingService<MessagingTests>>();

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
    public void ICanSendAndReceiveAMessage()
    {
        var messengerService = _serviceProvider!.GetRequiredService<IMessengerService>();
        var loggingService = _serviceProvider!.GetRequiredService<ILoggingService>();

        bool received = false;
        messengerService.Register<TestMessage>(this, (r, m) =>
        {
            received = true;
        });

        var message = new TestMessage();
        messengerService.Send(message);

        received.Should().BeTrue();
    }
}
