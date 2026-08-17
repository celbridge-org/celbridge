using System.Text.Json;
using Celbridge.FileSystem.Services;
using Celbridge.Reports;
using Celbridge.Tests.Migration.TestHelpers;
using Celbridge.Utilities;

namespace Celbridge.Tests.Reports;

/// <summary>
/// Unit tests for ReportWriter — serializing a report document, stamping its filename with
/// the generation time so a written report is never overwritten, and pruning the oldest
/// reports that share an id.
/// </summary>
[TestFixture]
public class ReportWriterTests
{
    private string _reportsFolderPath = null!;
    private ReportWriter _reportWriter = null!;

    [SetUp]
    public void Setup()
    {
        _reportsFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(ReportWriterTests),
            Guid.NewGuid().ToString("N"));

        var fileSystem = new LocalFileSystem(MigrationTestHelper.CreateMockLogger<LocalFileSystem>());
        _reportWriter = new ReportWriter(fileSystem, MigrationTestHelper.CreateMockLogger<ReportWriter>());
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_reportsFolderPath))
        {
            try
            {
                Directory.Delete(_reportsFolderPath, recursive: true);
            }
            catch
            {
                // Best effort
            }
        }
    }

    [Test]
    public async Task WriteReportAsync_WritesTheCurrentReportAtAStableName()
    {
        // The path a report is opened by carries no timestamp, so the reader sees a name they can
        // recognise and an already-open report is the same resource on every run.
        var generatedAt = new DateTimeOffset(2026, 8, 16, 14, 32, 11, TimeSpan.Zero);
        var report = CreateReport("project-load", generatedAt);

        var result = await _reportWriter.WriteReportAsync(report, _reportsFolderPath);

        result.IsSuccess.Should().BeTrue();
        Path.GetFileName(result.Value).Should().Be("project-load.report");
    }

    [Test]
    public async Task WriteReportAsync_ASecondRun_ArchivesTheFirstUnderItsGenerationTime()
    {
        await _reportWriter.WriteReportAsync(
            CreateReport("project-load", new DateTimeOffset(2026, 8, 16, 10, 0, 0, TimeSpan.Zero)),
            _reportsFolderPath);
        await _reportWriter.WriteReportAsync(
            CreateReport("project-load", new DateTimeOffset(2026, 8, 16, 11, 0, 0, TimeSpan.Zero)),
            _reportsFolderPath);

        GetReportFileNames().Should().Equal("project-load.report");
        GetHistoryFileNames().Should().Equal("project-load-20260816T100000Z.report");
    }

    [Test]
    public async Task WriteReportAsync_RewritingOneGeneration_DoesNotArchiveIt()
    {
        // A producer that flushes several times during one operation is revising a single report, so
        // the intermediate states are not history.
        var generatedAt = new DateTimeOffset(2026, 8, 16, 10, 0, 0, TimeSpan.Zero);

        await _reportWriter.WriteReportAsync(CreateReport("project-load", generatedAt), _reportsFolderPath);
        await _reportWriter.WriteReportAsync(CreateReport("project-load", generatedAt), _reportsFolderPath);
        await _reportWriter.WriteReportAsync(CreateReport("project-load", generatedAt), _reportsFolderPath);

        GetReportFileNames().Should().Equal("project-load.report");
        GetHistoryFileNames().Should().BeEmpty();
    }

    [Test]
    public async Task WriteReportAsync_BeyondTheRetentionLimit_PrunesTheOldestHistoryEntry()
    {
        var generatedAt = new DateTimeOffset(2026, 8, 16, 9, 0, 0, TimeSpan.Zero);

        for (var writeIndex = 0; writeIndex < ReportWriter.RetainCount + 3; writeIndex++)
        {
            var report = CreateReport("project-load", generatedAt.AddMinutes(writeIndex));
            await _reportWriter.WriteReportAsync(report, _reportsFolderPath);
        }

        var historyFileNames = GetHistoryFileNames();
        historyFileNames.Should().HaveCount(ReportWriter.RetainCount);

        // The survivors are the newest, so the earliest timestamps are the ones that went.
        historyFileNames.Should().NotContain("project-load-20260816T090000Z.report");
        historyFileNames.Should().Contain("project-load-20260816T090600Z.report");

        // The last generation written is the current report, not a history entry.
        GetReportFileNames().Should().Equal("project-load.report");
        historyFileNames.Should().NotContain("project-load-20260816T090700Z.report");
    }

    [Test]
    public async Task WriteReportAsync_PruningIsScopedToTheReportId()
    {
        var generatedAt = new DateTimeOffset(2026, 8, 16, 9, 0, 0, TimeSpan.Zero);

        for (var writeIndex = 0; writeIndex < ReportWriter.RetainCount + 2; writeIndex++)
        {
            var report = CreateReport("project-load", generatedAt.AddMinutes(writeIndex));
            await _reportWriter.WriteReportAsync(report, _reportsFolderPath);
        }

        await _reportWriter.WriteReportAsync(CreateReport("resource-move", generatedAt), _reportsFolderPath);
        await _reportWriter.WriteReportAsync(
            CreateReport("resource-move", generatedAt.AddMinutes(1)),
            _reportsFolderPath);

        GetReportFileNames().Should().BeEquivalentTo(new[] { "project-load.report", "resource-move.report" });
        GetHistoryFileNames().Should().Contain("resource-move-20260816T090000Z.report");
    }

    [Test]
    public async Task WriteReportAsync_WritesTheSchemaVersionAndCamelCaseFields()
    {
        var report = CreateReport("project-load", DateTimeOffset.UtcNow);

        var result = await _reportWriter.WriteReportAsync(report, _reportsFolderPath);

        var content = await File.ReadAllTextAsync(result.Value);
        using var document = JsonDocument.Parse(content);

        document.RootElement.GetProperty("version").GetInt32().Should().Be(ReportDocument.CurrentVersion);
        document.RootElement.GetProperty("id").GetString().Should().Be("project-load");
        document.RootElement.GetProperty("severity").GetString().Should().Be("info");

        // Optional fields are omitted rather than written as null, so a report stays readable.
        document.RootElement.TryGetProperty("truncated", out _).Should().BeFalse();
    }

    [Test]
    public async Task WriteReportAsync_EmptyReportId_Fails()
    {
        var report = CreateReport(string.Empty, DateTimeOffset.UtcNow);

        var result = await _reportWriter.WriteReportAsync(report, _reportsFolderPath);

        result.IsFailure.Should().BeTrue();
    }

    private static ReportDocument CreateReport(string reportId, DateTimeOffset generatedAt)
    {
        var items = new List<ReportItem>
        {
            new ReportItem(ReportSeverity.Info, "Resources")
            {
                Value = "12 files in 3 folders"
            }
        };

        var sections = new List<ReportSection>
        {
            new ReportSection("Summary", ReportSectionKind.Facts, ReportSeverity.Info, items)
        };

        return new ReportDocument(
            reportId,
            "Project Load",
            generatedAt,
            ReportSeverity.Info,
            "The project loaded with no issues.",
            sections);
    }

    private List<string> GetReportFileNames()
    {
        return ListReportFileNames(_reportsFolderPath);
    }

    private List<string> GetHistoryFileNames()
    {
        var historyFolderPath = Path.Combine(_reportsFolderPath, ReportWriter.HistoryFolderName);
        if (!Directory.Exists(historyFolderPath))
        {
            return new List<string>();
        }

        return ListReportFileNames(historyFolderPath);
    }

    private static List<string> ListReportFileNames(string folderPath)
    {
        return Directory.GetFiles(folderPath, "*.report")
            .Select(Path.GetFileName)
            .Where(fileName => fileName is not null)
            .Select(fileName => fileName!)
            .ToList();
    }
}
