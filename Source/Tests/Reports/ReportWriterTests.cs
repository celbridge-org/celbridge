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
    public async Task WriteReportAsync_StampsTheGenerationTimeIntoTheFileName()
    {
        var generatedAt = new DateTimeOffset(2026, 8, 16, 14, 32, 11, TimeSpan.Zero);
        var report = CreateReport("project-load", generatedAt);

        var result = await _reportWriter.WriteReportAsync(report, _reportsFolderPath);

        result.IsSuccess.Should().BeTrue();
        Path.GetFileName(result.Value).Should().Be("project-load-20260816T143211Z.report");
    }

    [Test]
    public async Task WriteReportAsync_TwoRunsOfTheSameOperation_WritesTwoFiles()
    {
        // A report is a record of one completed run, so a second run never overwrites the
        // first. This is also what keeps an already-open report from changing underneath
        // the reader on an unwatched root.
        await _reportWriter.WriteReportAsync(
            CreateReport("project-load", new DateTimeOffset(2026, 8, 16, 10, 0, 0, TimeSpan.Zero)),
            _reportsFolderPath);
        await _reportWriter.WriteReportAsync(
            CreateReport("project-load", new DateTimeOffset(2026, 8, 16, 11, 0, 0, TimeSpan.Zero)),
            _reportsFolderPath);

        GetReportFileNames().Should().HaveCount(2);
    }

    [Test]
    public async Task WriteReportAsync_BeyondTheRetentionLimit_PrunesTheOldest()
    {
        var generatedAt = new DateTimeOffset(2026, 8, 16, 9, 0, 0, TimeSpan.Zero);

        for (var writeIndex = 0; writeIndex < ReportWriter.RetainCount + 3; writeIndex++)
        {
            var report = CreateReport("project-load", generatedAt.AddMinutes(writeIndex));
            await _reportWriter.WriteReportAsync(report, _reportsFolderPath);
        }

        var fileNames = GetReportFileNames();
        fileNames.Should().HaveCount(ReportWriter.RetainCount);

        // The survivors are the newest, so the earliest timestamps are the ones that went.
        fileNames.Should().NotContain("project-load-20260816T090000Z.report");
        fileNames.Should().Contain("project-load-20260816T090700Z.report");
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

        var otherReport = CreateReport("resource-move", generatedAt);
        await _reportWriter.WriteReportAsync(otherReport, _reportsFolderPath);

        var fileNames = GetReportFileNames();
        fileNames.Should().HaveCount(ReportWriter.RetainCount + 1);
        fileNames.Should().Contain("resource-move-20260816T090000Z.report");
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
        return Directory.GetFiles(_reportsFolderPath, "*.report")
            .Select(Path.GetFileName)
            .Where(fileName => fileName is not null)
            .Select(fileName => fileName!)
            .ToList();
    }
}
