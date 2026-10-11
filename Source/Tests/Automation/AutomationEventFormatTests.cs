using System.Text.Json;
using Celbridge.Automation;
using Celbridge.Automation.Services;
using Celbridge.Tests.Helpers;

namespace Celbridge.Tests.Automation;

/// <summary>
/// Tests for the log line an automation event is written as, and for an event that cannot be written.
/// </summary>
[TestFixture]
public class AutomationEventFormatTests
{
    private enum TestState
    {
        Ready
    }

    private record NoFieldsEvent : AutomationEvent;

    private record StateEvent(TestState State) : AutomationEvent;

    // The serializer refuses a Type.
    private record UnwritableEvent(Type Kind) : AutomationEvent;

    [Test]
    public void AnEvent_IsItsRecordNameFollowedByItsFieldsInCamelCase()
    {
        var line = AutomationService.FormatEvent(new ServerStartedEvent(5123));

        line.Should().Be("""ServerStartedEvent {"port":5123}""");
    }

    [Test]
    public void AnEventWithNoFields_WritesAnEmptyObject()
    {
        var line = AutomationService.FormatEvent(new NoFieldsEvent());

        line.Should().Be("NoFieldsEvent {}");
    }

    [Test]
    public void AnEnumField_IsWrittenAsItsName()
    {
        var line = AutomationService.FormatEvent(new StateEvent(TestState.Ready));

        line.Should().Be("""StateEvent {"state":"Ready"}""");
    }

    [Test]
    public void TextWithQuotesAndLineBreaks_StaysOnOneLineAndParsesBack()
    {
        var title = "Can't open 'Project'";
        var message = "It needs \"v99.0.0\".\nThis is v1.0.0.";

        var line = AutomationService.FormatEvent(new AlertShownEvent(title, message));

        line.Should().NotContain("\n");
        line.Should().StartWith("AlertShownEvent {");

        var fields = JsonDocument.Parse(line["AlertShownEvent ".Length..]).RootElement;
        fields.GetProperty("title").GetString().Should().Be(title);
        fields.GetProperty("message").GetString().Should().Be(message);
    }

    [Test]
    public void AnEventThatCannotBeWritten_IsLoggedAsAnErrorInsteadOfThrowing()
    {
        var logger = new RecordingLogger<AutomationService>();
        var automationService = new AutomationService(logger);

        var logEvent = () => automationService.LogEvent(new UnwritableEvent(typeof(string)));

        logEvent.Should().NotThrow();
        logger.EntriesAt(LogEntryLevel.Debug).Should().BeEmpty();
        logger.EntriesAt(LogEntryLevel.Error).Should().ContainSingle();
    }
}
