using Celbridge.Dialog;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.UserInterface.Services.Dialogs;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Covers the progress dialog's timing log. The controller logs how long each token was held. A token that stays
/// held for a long time gets a warning.
/// </summary>
[TestFixture]
public class ProgressDialogControllerTests
{
    private static readonly TimeSpan WarningInterval = TimeSpan.FromMilliseconds(100);

    private ILogger _logger = null!;
    private IMessengerService _messengerService = null!;
    private ProgressDialogController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _logger = Substitute.For<ILogger>();
        _messengerService = new MessengerService();

        _controller = new ProgressDialogController(_logger, Substitute.For<IDialogFactory>(), _messengerService)
        {
            WarningInterval = WarningInterval
        };
    }

    [Test]
    public void ReleasingAToken_LogsHowLongTheTokenWasHeld()
    {
        var token = _controller.Acquire("Working");
        token.Dispose();

        _logger.Received().LogInformation(Arg.Is<string?>(message => IsEndedEntry(message)), Arg.Any<object?[]>());
    }

    [Test]
    public async Task ATokenHeldForLong_IsReported()
    {
        using var token = _controller.Acquire("Working");

        await Task.Delay(WarningInterval * 3);

        _logger.Received().LogWarning(Arg.Is<string?>(message => IsHeldWarning(message)), Arg.Any<object?[]>());
    }

    [Test]
    public async Task ATokenReleasedQuickly_IsNotReported()
    {
        var token = _controller.Acquire("Working");
        token.Dispose();

        await Task.Delay(WarningInterval * 3);

        _logger.DidNotReceive().LogWarning(Arg.Is<string?>(message => IsHeldWarning(message)), Arg.Any<object?[]>());
    }

    [Test]
    public void ReleasingAToken_SendsTheReleasedMessageBeforeTheTokenStopsCounting()
    {
        // A listener records the release time from the message. The listener then has the release time before it can
        // see the progress dialog gone.
        var token = _controller.Acquire("Working");

        bool? activeWhenSent = null;
        _messengerService.Register<ProgressDialogReleasedMessage>(this, (_, _) =>
        {
            activeWhenSent = _controller.IsActive;
        });

        token.Dispose();

        activeWhenSent.Should().BeTrue();
        _controller.IsActive.Should().BeFalse();
    }

    private static bool IsEndedEntry(string? message)
    {
        return message is not null
            && message.Contains("ended after");
    }

    private static bool IsHeldWarning(string? message)
    {
        return message is not null
            && message.Contains("without ending");
    }
}
