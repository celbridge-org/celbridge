using Celbridge.Dialog;
using Celbridge.Documents;
using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Reports;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Tests for the notification half of the editor bridge. A contribution's only route to the user is
/// through here, so what it accepts and what it refuses is the contract.
/// </summary>
[TestFixture]
public class CustomDialogHandlerTests
{
    private IMessengerService _messengerService = null!;
    private CustomDialogHandler _handler = null!;

    private readonly List<EditorNotificationMessage> _sentMessages = new();

    [SetUp]
    public void Setup()
    {
        _messengerService = new MessengerService();
        _messengerService.Register<EditorNotificationMessage>(this, (_, message) => _sentMessages.Add(message));

        _handler = new CustomDialogHandler(
            Substitute.For<IDialogService>(),
            Substitute.For<IStringLocalizer>(),
            _messengerService,
            null!);
    }

    [TearDown]
    public void TearDown()
    {
        _messengerService.UnregisterAll(this);
        _sentMessages.Clear();
    }

    [TestCase("info", ReportSeverity.Info)]
    [TestCase("warning", ReportSeverity.Warning)]
    [TestCase("error", ReportSeverity.Error)]
    [TestCase("Error", ReportSeverity.Error)]
    public async Task ARecognisedSeverity_RaisesTheNotification(string severity, ReportSeverity expected)
    {
        await _handler.NotifyAsync(severity, "9 of 40 tilesets failed to convert");

        _sentMessages.Should().HaveCount(1);
        _sentMessages[0].Severity.Should().Be(expected);
        _sentMessages[0].Message.Should().Be("9 of 40 tilesets failed to convert");
    }

    [Test]
    public async Task AnUnknownSeverity_IsRefusedRatherThanDowngraded()
    {
        // Showing an intended error as information is the worse failure, so the editor is told.
        var act = async () => await _handler.NotifyAsync("critical", "something went wrong");

        await act.Should().ThrowAsync<ArgumentException>();

        _sentMessages.Should().BeEmpty();
    }

    [Test]
    public async Task AnEmptyMessage_IsRefused()
    {
        var act = async () => await _handler.NotifyAsync("info", "   ");

        await act.Should().ThrowAsync<ArgumentException>();

        _sentMessages.Should().BeEmpty();
    }
}
