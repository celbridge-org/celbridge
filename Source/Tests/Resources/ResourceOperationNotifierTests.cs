using Celbridge.Tests.Localization;
using System.Text.Json;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Projects;
using Celbridge.Reports;
using Celbridge.Resources;
using Celbridge.Resources.Helpers;
using Celbridge.Tests.FileSystem;
using Celbridge.Utilities;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for the notifier every resource operation reports its failures through. What it decides is
/// whether the failures are worth a report: one is fully expressed by the notification line, several
/// are not.
/// </summary>
[TestFixture]
public class ResourceOperationNotifierTests
{
    private string _projectFolderPath = null!;
    private IMessengerService _messengerService = null!;
    private ResourceOperationNotifier _notifier = null!;

    private readonly List<ResourceOperationFailedMessage> _sentMessages = new();

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(ResourceOperationNotifierTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectFolderPath);

        _messengerService = new MessengerService();
        _messengerService.Register<ResourceOperationFailedMessage>(this, (_, message) => _sentMessages.Add(message));

        var project = Substitute.For<IProject>();
        project.ProjectFilePath.Returns(Path.Combine(_projectFolderPath, "Project.celbridge"));

        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        var reportWriter = new ReportWriter(
            TestFileSystem.CreateLocal(),
            Substitute.For<ILogger<ReportWriter>>());

        _notifier = new ResourceOperationNotifier(
            Substitute.For<ILogger<ResourceOperationNotifier>>(),
            _messengerService,
            projectService,
            reportWriter,
            new TestLocalizerService());
    }

    [TearDown]
    public void TearDown()
    {
        _messengerService.UnregisterAll(this);
        _sentMessages.Clear();

        if (Directory.Exists(_projectFolderPath))
        {
            try
            {
                Directory.Delete(_projectFolderPath, true);
            }
            catch
            {
                // Best effort
            }
        }
    }

    [Test]
    public async Task NoFailures_RaiseNothing()
    {
        await _notifier.NotifyFailuresAsync(ResourceOperationType.Delete, Array.Empty<FailedResource>());

        _sentMessages.Should().BeEmpty();
    }

    [Test]
    public async Task OneFailure_IsNotifiedWithoutAReport()
    {
        var failedResources = new List<FailedResource>
        {
            new FailedResource(new ResourceKey("notes.txt"), "the file is locked")
        };

        await _notifier.NotifyFailuresAsync(ResourceOperationType.Delete, failedResources);

        _sentMessages.Should().HaveCount(1);
        _sentMessages[0].FailedResources.Should().HaveCount(1);
        _sentMessages[0].ReportResource.IsEmpty.Should().BeTrue();

        Directory.Exists(ReportLocation.ResolveFolderPath(Path.Combine(_projectFolderPath, "Project.celbridge")))
            .Should().BeFalse("one failure says everything the report would have said");
    }

    [Test]
    public async Task SeveralFailures_AreWrittenAsAReportTheNotificationPointsAt()
    {
        var failedResources = new List<FailedResource>
        {
            new FailedResource(new ResourceKey("notes.txt"), "the file is locked"),
            new FailedResource(new ResourceKey("data/table.csv"), "permission denied")
        };

        await _notifier.NotifyFailuresAsync(ResourceOperationType.Delete, failedResources);

        _sentMessages.Should().HaveCount(1);
        var reportResource = _sentMessages[0].ReportResource;
        reportResource.ToString().Should().Be("logs:reports/delete-resources.report");

        var report = ReadReport("delete-resources.report");
        report.GetProperty("severity").GetString().Should().Be("error");

        var sections = report.GetProperty("sections").EnumerateArray().ToList();
        sections.Should().HaveCount(1);

        var items = sections[0].GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(2);
        items[0].GetProperty("code").GetString()
            .Should().Be(ReportFindingCatalog.Resource.DeleteFailed.Code);
        items[0].GetProperty("detail").GetString().Should().Be("the file is locked");
        items[0].GetProperty("actions").EnumerateArray().First()
            .GetProperty("resource").GetString().Should().Be("project:notes.txt");
    }

    [Test]
    public async Task AMoveThatLeftReferencesStale_ReportsThemBesideTheFailures()
    {
        var failedResources = new List<FailedResource>
        {
            new FailedResource(new ResourceKey("notes.txt"), "the file is locked")
        };

        var skippedReferencers = new List<SkippedReferencer>
        {
            new SkippedReferencer(new ResourceKey("index.json"), ReferencerSkipReason.ReadOnly, "read-only")
        };

        await _notifier.NotifyFailuresAsync(ResourceOperationType.Move, failedResources, skippedReferencers);

        var report = ReadReport("move-resources.report");
        var sections = report.GetProperty("sections").EnumerateArray().ToList();
        sections.Should().HaveCount(2);
        sections[0].GetProperty("title").GetString().Should().Be("Resources");
        sections[1].GetProperty("title").GetString().Should().Be("References");

        var referenceItem = sections[1].GetProperty("items").EnumerateArray().First();
        referenceItem.GetProperty("code").GetString()
            .Should().Be(ReportFindingCatalog.Resource.ReferenceNotUpdated.Code);
    }

    [Test]
    public async Task StaleReferencesAlone_AreAWarningRatherThanAFailure()
    {
        var skippedReferencers = new List<SkippedReferencer>
        {
            new SkippedReferencer(new ResourceKey("a.json"), ReferencerSkipReason.ReadOnly, "read-only"),
            new SkippedReferencer(new ResourceKey("b.json"), ReferencerSkipReason.ReadOnly, "read-only")
        };

        await _notifier.NotifyFailuresAsync(
            ResourceOperationType.Move,
            Array.Empty<FailedResource>(),
            skippedReferencers);

        _sentMessages.Should().HaveCount(1);
        _sentMessages[0].FailedResources.Should().BeEmpty();
        _sentMessages[0].SkippedReferencers.Should().HaveCount(2);

        var report = ReadReport("move-resources.report");
        report.GetProperty("severity").GetString().Should().Be("warning");
    }

    [Test]
    public async Task ASummaryOfSeveralFailures_ReadsAsOneSentenceForTheOperation()
    {
        var failedResources = new List<FailedResource>
        {
            new FailedResource(new ResourceKey("a.txt"), "the file is locked"),
            new FailedResource(new ResourceKey("b.txt"), "the file is locked")
        };

        await _notifier.NotifyFailuresAsync(ResourceOperationType.Delete, failedResources);

        var report = ReadReport("delete-resources.report");
        report.GetProperty("summary").GetString()
            .Should().Be("2 resources could not be deleted.");
    }

    [Test]
    public async Task ASummaryOfFailuresAndStaleReferences_ReadsAsASentenceForEach()
    {
        var failedResources = new List<FailedResource>
        {
            new FailedResource(new ResourceKey("notes.txt"), "the file is locked")
        };

        var skippedReferencers = new List<SkippedReferencer>
        {
            new SkippedReferencer(new ResourceKey("index.json"), ReferencerSkipReason.ReadOnly, "read-only")
        };

        await _notifier.NotifyFailuresAsync(ResourceOperationType.Move, failedResources, skippedReferencers);

        var report = ReadReport("move-resources.report");
        report.GetProperty("summary").GetString()
            .Should().Be("1 resource could not be moved. 1 reference was left pointing at the old location.");
    }

    [Test]
    public async Task ASummaryOfStaleReferencesAlone_SaysTheOperationItselfCompleted()
    {
        var skippedReferencers = new List<SkippedReferencer>
        {
            new SkippedReferencer(new ResourceKey("a.json"), ReferencerSkipReason.ReadOnly, "read-only"),
            new SkippedReferencer(new ResourceKey("b.json"), ReferencerSkipReason.ReadOnly, "read-only")
        };

        await _notifier.NotifyFailuresAsync(
            ResourceOperationType.Move,
            Array.Empty<FailedResource>(),
            skippedReferencers);

        var report = ReadReport("move-resources.report");
        report.GetProperty("summary").GetString()
            .Should().Be("The operation completed. 2 references were left pointing at the old location.");
    }

    [Test]
    public async Task AnOperationThatCannotBatch_WritesNoReportHoweverManyFailed()
    {
        // Archiving names one source and one archive, so there is no id for its history to group by.
        var failedResources = new List<FailedResource>
        {
            new FailedResource(new ResourceKey("a.zip"), "disk full"),
            new FailedResource(new ResourceKey("b.zip"), "disk full")
        };

        await _notifier.NotifyFailuresAsync(ResourceOperationType.Archive, failedResources);

        _sentMessages.Should().HaveCount(1);
        _sentMessages[0].ReportResource.IsEmpty.Should().BeTrue();
    }

    private JsonElement ReadReport(string reportFileName)
    {
        var reportsFolderPath = ReportLocation.ResolveFolderPath(
            Path.Combine(_projectFolderPath, "Project.celbridge"));

        var reportPath = Path.Combine(reportsFolderPath, reportFileName);
        File.Exists(reportPath).Should().BeTrue();

        using var document = JsonDocument.Parse(File.ReadAllText(reportPath));

        return document.RootElement.Clone();
    }
}
