using System.Text.Json;
using Celbridge.FileSystem.Services;
using Celbridge.Reports;
using Celbridge.Tests.Migration.TestHelpers;
using Celbridge.Utilities;

namespace Celbridge.Tests.Reports;

/// <summary>
/// Unit tests for ReportWriter — serializing a report document, stamping its filename with
/// the generation time so a written report is never overwritten, and sweeping the history
/// entries that have outlived the retention period.
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
    public async Task WriteReportAsync_SweepsHistoryEntriesPastTheRetentionPeriod()
    {
        var generatedAt = new DateTimeOffset(2026, 8, 16, 9, 0, 0, TimeSpan.Zero);

        await _reportWriter.WriteReportAsync(CreateReport("project-load", generatedAt), _reportsFolderPath);
        await _reportWriter.WriteReportAsync(
            CreateReport("project-load", generatedAt.AddMinutes(1)),
            _reportsFolderPath);

        // The sweep reads the file's own write time, so the entry is aged rather than the clock moved.
        AgeHistoryEntry("project-load-20260816T090000Z.report", ReportWriter.RetentionPeriod.Add(TimeSpan.FromDays(1)));

        await _reportWriter.WriteReportAsync(
            CreateReport("project-load", generatedAt.AddMinutes(2)),
            _reportsFolderPath);

        // The aged entry went; the one archived by the write that triggered the sweep stayed.
        GetHistoryFileNames().Should().Equal("project-load-20260816T090100Z.report");
        GetReportFileNames().Should().Equal("project-load.report");
    }

    [Test]
    public async Task WriteReportAsync_LeavesHistoryEntriesWithinTheRetentionPeriod()
    {
        var generatedAt = new DateTimeOffset(2026, 8, 16, 9, 0, 0, TimeSpan.Zero);

        for (var writeIndex = 0; writeIndex < 8; writeIndex++)
        {
            var report = CreateReport("project-load", generatedAt.AddMinutes(writeIndex));
            await _reportWriter.WriteReportAsync(report, _reportsFolderPath);
        }

        // Nothing is capped by count, so every generation but the current one is still there.
        GetHistoryFileNames().Should().HaveCount(7);
        GetHistoryFileNames().Should().Contain("project-load-20260816T090000Z.report");
        GetReportFileNames().Should().Equal("project-load.report");
    }

    [Test]
    public async Task WriteReportAsync_ABusyIdDoesNotDisplaceTheHistoryOfAnIdSharingItsPrefix()
    {
        // "acme-tiles-*" also matches every history entry belonging to "acme-tiles-convert", so a
        // per-id glob counted the busy id's entries against the quiet one and swept the quiet one out.
        var generatedAt = new DateTimeOffset(2026, 8, 16, 9, 0, 0, TimeSpan.Zero);

        // The busy neighbour runs first, so its entries are already in the folder when the quiet id
        // archives its own. That is the order the glob crossed them in.
        for (var writeIndex = 0; writeIndex < 8; writeIndex++)
        {
            var report = CreateReport("acme-tiles-convert", generatedAt.AddMinutes(writeIndex));
            await _reportWriter.WriteReportAsync(report, _reportsFolderPath);
        }

        await _reportWriter.WriteReportAsync(CreateReport("acme-tiles", generatedAt), _reportsFolderPath);
        await _reportWriter.WriteReportAsync(
            CreateReport("acme-tiles", generatedAt.AddMinutes(1)),
            _reportsFolderPath);

        // The quiet id's one entry is still there, however busy its prefix-sharing neighbour was.
        GetHistoryFileNames().Should().Contain("acme-tiles-20260816T090000Z.report");
        GetReportFileNames().Should().BeEquivalentTo(new[] { "acme-tiles.report", "acme-tiles-convert.report" });
    }

    [Test]
    public async Task WriteReportAsync_WithNoGenerationStamp_StampsTheWriteTime()
    {
        // A contribution that leaves generatedAt out would otherwise write the default stamp on every
        // run, which reads as one generation being revised and so never rotates.
        var writtenAt = DateTimeOffset.UtcNow;

        await _reportWriter.WriteReportAsync(CreateReport("acme-convert", default), _reportsFolderPath);
        await _reportWriter.WriteReportAsync(CreateReport("acme-convert", default), _reportsFolderPath);

        var content = await File.ReadAllTextAsync(Path.Combine(_reportsFolderPath, "acme-convert.report"));
        using var document = JsonDocument.Parse(content);

        var stamp = document.RootElement.GetProperty("generatedAt").GetDateTimeOffset();
        stamp.Should().BeOnOrAfter(writtenAt);

        // Two runs, so the first is history rather than having been overwritten in place.
        GetHistoryFileNames().Should().HaveCount(1);
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

    // Backdates a history entry so the age sweep sees it as expired.
    private void AgeHistoryEntry(string historyFileName, TimeSpan age)
    {
        var historyFilePath = Path.Combine(_reportsFolderPath, ReportWriter.HistoryFolderName, historyFileName);

        File.SetLastWriteTimeUtc(historyFilePath, DateTime.UtcNow - age);
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
