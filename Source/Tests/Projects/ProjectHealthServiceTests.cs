using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Reports;

namespace Celbridge.Tests.Projects;

/// <summary>
/// The switcher's health indicator reads from this service, so what it holds is what the user is told
/// about their project. It reflects the current load and stops reflecting anything once that project
/// is gone.
/// </summary>
[TestFixture]
public class ProjectHealthServiceTests
{
    private IMessengerService _messengerService = null!;
    private ProjectHealthService _service = null!;

    [SetUp]
    public void Setup()
    {
        _messengerService = Substitute.For<IMessengerService>();
        _service = new ProjectHealthService(_messengerService);
    }

    [Test]
    public void NoProjectLoaded_HasNoHealth()
    {
        _service.CurrentHealth.Should().BeNull();
    }

    [Test]
    public void SetHealth_HoldsItAndAnnouncesTheChange()
    {
        var summary = CreateSummary(ReportSeverity.Warning, issueCount: 3);

        _service.SetHealth(summary);

        _service.CurrentHealth.Should().Be(summary);
        _messengerService.Received(1).Send(
            Arg.Is<ProjectHealthChangedMessage>(message => message.Health == summary));
    }

    [Test]
    public void AHealthyLoad_IsStillRecorded()
    {
        // The health row states "no issues" and opens the report in that state too, so a clean load is
        // recorded rather than left looking like no load happened.
        var summary = CreateSummary(ReportSeverity.Info, issueCount: 0);

        _service.SetHealth(summary);

        _service.CurrentHealth.Should().Be(summary);
    }

    [Test]
    public void ClearHealth_DropsItAndAnnouncesTheChange()
    {
        _service.SetHealth(CreateSummary(ReportSeverity.Error, issueCount: 1));
        _messengerService.ClearReceivedCalls();

        _service.ClearHealth();

        _service.CurrentHealth.Should().BeNull();
        _messengerService.Received(1).Send(
            Arg.Is<ProjectHealthChangedMessage>(message => message.Health == null));
    }

    [Test]
    public void ClearHealth_WithNothingRecorded_AnnouncesNothing()
    {
        _service.ClearHealth();

        _messengerService.DidNotReceive().Send(Arg.Any<ProjectHealthChangedMessage>());
    }

    private static ProjectLoadReportSummary CreateSummary(ReportSeverity severity, int issueCount)
    {
        return new ProjectLoadReportSummary(
            new ResourceKey("logs:reports/project-load.report"),
            severity,
            issueCount);
    }
}
