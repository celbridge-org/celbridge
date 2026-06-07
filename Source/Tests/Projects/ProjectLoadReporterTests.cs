using Celbridge.FileSystem.Services;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Resources;
using Celbridge.Tests.Migration.TestHelpers;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Unit tests for ProjectLoadReporter — the stateful singleton that
/// accumulates project-load events from ProjectLoader plus consistency-check
/// findings from ProjectCheckCommand and writes one Markdown report on
/// FlushAsync. The tests pin the file path, the section layout, and the
/// state-reset semantics of BeginLoad.
/// </summary>
[TestFixture]
public class ProjectLoadReporterTests
{
    private string _projectFolderPath = null!;
    private string _projectFilePath = null!;
    private ProjectLoadReporter _reporter = null!;
    private ILocalFileSystem _fileSystem = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(ProjectLoadReporterTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectFolderPath);

        _projectFilePath = Path.Combine(_projectFolderPath, "test.celbridge");

        _fileSystem = new LocalFileSystem(MigrationTestHelper.CreateMockLogger<LocalFileSystem>());
        _reporter = new ProjectLoadReporter(_fileSystem, MigrationTestHelper.CreateMockLogger<ProjectLoadReporter>());
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_projectFolderPath))
        {
            try
            {
                Directory.Delete(_projectFolderPath, recursive: true);
            }
            catch
            {
                // Best effort
            }
        }
    }

    [Test]
    public async Task FlushAsync_LandsAtCelbridgeLogsFolder()
    {
        // The on-disk location is implementation-internal but pinned here so
        // the alert text ("see the project load report") stays meaningful.
        _reporter.BeginLoad(_projectFilePath);
        _reporter.RecordLoadOutcome(loadSucceeded: true, loadResult: Result.Ok());

        var reportPath = await _reporter.FlushAsync();

        var expected = Path.Combine(_projectFolderPath, ".celbridge", "logs", "project-load.md");
        reportPath.Should().Be(expected);
    }

    [Test]
    public async Task FlushAsync_WithoutBeginLoad_ReturnsNullAndWritesNothing()
    {
        // Without a project context, there is nothing to write and the path
        // cannot be derived. The reporter no-ops rather than creating an empty
        // file under an arbitrary path.
        var result = await _reporter.FlushAsync();

        result.Should().BeNull();
        Directory.Exists(Path.Combine(_projectFolderPath, ".celbridge")).Should().BeFalse();
    }

    [Test]
    public async Task FlushAsync_AfterLoadOnly_WritesLoadSectionWithoutCheckSection()
    {
        _reporter.BeginLoad(_projectFilePath);
        _reporter.RecordMigrationResult(
            MigrationResult.WithVersions(MigrationStatus.Complete, Result.Ok(), "0.2.7", "1.0.0"),
            userConfirmedUpgrade: false,
            userCancelledUpgrade: false);
        _reporter.RecordLoadOutcome(loadSucceeded: true, loadResult: Result.Ok());

        var reportPath = await _reporter.FlushAsync();

        reportPath.Should().NotBeNull();
        File.Exists(reportPath).Should().BeTrue();

        var content = await File.ReadAllTextAsync(reportPath!);
        content.Should().Contain("# Project load report");
        content.Should().Contain($"- Project: `{_projectFilePath}`");
        content.Should().Contain("- Outcome: success");
        content.Should().Contain("## Load");
        content.Should().Contain("Migration status: `Complete`");
        content.Should().NotContain("## Consistency check");
    }

    [Test]
    public async Task FlushAsync_FailedLoad_IncludesErrorChainAndDiagnostics()
    {
        var migrationFailure = Result.Fail("Failed to parse project TOML file: (1,12) : error : Invalid \\r not followed by \\n");
        var loadFailure = Result.Fail("Failed to load project: 'test.celbridge'").WithErrors(migrationFailure);

        _reporter.BeginLoad(_projectFilePath);
        _reporter.RecordMigrationResult(
            MigrationResult.FromStatus(MigrationStatus.InvalidConfig, migrationFailure),
            userConfirmedUpgrade: false,
            userCancelledUpgrade: false);
        _reporter.RecordLoadOutcome(loadSucceeded: false, loadResult: loadFailure);

        var reportPath = await _reporter.FlushAsync();

        reportPath.Should().NotBeNull();
        var content = await File.ReadAllTextAsync(reportPath!);

        content.Should().Contain("- Outcome: failed");
        content.Should().Contain("Migration status: `InvalidConfig`");
        content.Should().Contain("### Migration errors");
        content.Should().Contain("Invalid \\r not followed by \\n");
        content.Should().Contain("### Load errors");
        content.Should().Contain("Failed to load project");
        content.Should().Contain("Diagnostic chain");
    }

    [Test]
    public async Task FlushAsync_UserCancelledUpgrade_NotesCancellationInReport()
    {
        _reporter.BeginLoad(_projectFilePath);
        _reporter.RecordMigrationResult(
            MigrationResult.WithVersions(MigrationStatus.UpgradeRequired, Result.Ok(), "0.2.7", "1.0.0"),
            userConfirmedUpgrade: false,
            userCancelledUpgrade: true);
        _reporter.RecordLoadOutcome(loadSucceeded: false, loadResult: null);

        var reportPath = await _reporter.FlushAsync();

        reportPath.Should().NotBeNull();
        var content = await File.ReadAllTextAsync(reportPath!);
        content.Should().Contain("User cancelled the upgrade dialog");
    }

    [Test]
    public async Task FlushAsync_AfterRecordCheckReport_IncludesCheckSection()
    {
        // Mirrors the runtime flow: ProjectLoader pushes load info first, then
        // ProjectCheckCommand pushes the check report later, and each flush
        // rewrites the file end-to-end.
        _reporter.BeginLoad(_projectFilePath);
        _reporter.RecordMigrationResult(
            MigrationResult.WithVersions(MigrationStatus.Complete, Result.Ok(), "1.0.0", "1.0.0"),
            userConfirmedUpgrade: false,
            userCancelledUpgrade: false);
        _reporter.RecordLoadOutcome(loadSucceeded: true, loadResult: Result.Ok());

        var report = new ProjectCheckReport(
            BrokenReferences: new[] { new BrokenReference(new ResourceKey("source.json"), new ResourceKey("missing.json")) },
            OrphanCelFiles: new[] { new ResourceKey("foo.png.cel") },
            BrokenCelFiles: Array.Empty<ResourceKey>());
        _reporter.RecordCheckReport(report);

        var reportPath = await _reporter.FlushAsync();

        reportPath.Should().NotBeNull();
        var content = await File.ReadAllTextAsync(reportPath!);

        content.Should().Contain("## Load");
        content.Should().Contain("## Consistency check");
        content.Should().Contain("### Broken references (1)");
        content.Should().Contain("project:source.json");
        content.Should().Contain("project:missing.json");
        content.Should().Contain("### Orphan .cel files (1)");
        content.Should().Contain("project:foo.png.cel");
    }

    [Test]
    public async Task FlushAsync_CleanCheck_ReportsNoFindings()
    {
        _reporter.BeginLoad(_projectFilePath);
        _reporter.RecordMigrationResult(
            MigrationResult.WithVersions(MigrationStatus.Complete, Result.Ok(), "1.0.0", "1.0.0"),
            userConfirmedUpgrade: false,
            userCancelledUpgrade: false);
        _reporter.RecordLoadOutcome(loadSucceeded: true, loadResult: Result.Ok());

        var report = new ProjectCheckReport(
            BrokenReferences: Array.Empty<BrokenReference>(),
            OrphanCelFiles: Array.Empty<ResourceKey>(),
            BrokenCelFiles: Array.Empty<ResourceKey>());
        _reporter.RecordCheckReport(report);

        var reportPath = await _reporter.FlushAsync();

        reportPath.Should().NotBeNull();
        var content = await File.ReadAllTextAsync(reportPath!);
        content.Should().Contain("## Consistency check");
        content.Should().Contain("No findings");
    }

    [Test]
    public async Task BeginLoad_ClearsPriorCheckSection()
    {
        // A new project load invalidates the previous run's check state. The
        // load-report rewrites with just the new load section; the agent (or
        // user) has to run data_check_project again to repopulate the check
        // section.
        _reporter.BeginLoad(_projectFilePath);
        _reporter.RecordCheckReport(new ProjectCheckReport(
            BrokenReferences: Array.Empty<BrokenReference>(),
            OrphanCelFiles: new[] { new ResourceKey("stale.png.cel") },
            BrokenCelFiles: Array.Empty<ResourceKey>()));
        _reporter.RecordLoadOutcome(loadSucceeded: true, loadResult: Result.Ok());
        await _reporter.FlushAsync();

        // Second load picks up where the first left off — the prior check
        // findings should not bleed through.
        _reporter.BeginLoad(_projectFilePath);
        _reporter.RecordMigrationResult(
            MigrationResult.WithVersions(MigrationStatus.Complete, Result.Ok(), "1.0.0", "1.0.0"),
            userConfirmedUpgrade: false,
            userCancelledUpgrade: false);
        _reporter.RecordLoadOutcome(loadSucceeded: true, loadResult: Result.Ok());

        var reportPath = await _reporter.FlushAsync();

        reportPath.Should().NotBeNull();
        var content = await File.ReadAllTextAsync(reportPath!);
        content.Should().NotContain("## Consistency check");
        content.Should().NotContain("stale.png.cel");
    }

    [Test]
    public async Task FlushAsync_CreatesLogsFolderWhenMissing()
    {
        Directory.Exists(Path.Combine(_projectFolderPath, ".celbridge", "logs")).Should().BeFalse();

        _reporter.BeginLoad(_projectFilePath);
        _reporter.RecordMigrationResult(
            MigrationResult.FromStatus(MigrationStatus.Failed, Result.Fail("boom")),
            userConfirmedUpgrade: false,
            userCancelledUpgrade: false);
        _reporter.RecordLoadOutcome(loadSucceeded: false, loadResult: null);

        var reportPath = await _reporter.FlushAsync();

        reportPath.Should().NotBeNull();
        File.Exists(reportPath!).Should().BeTrue();
    }
}
