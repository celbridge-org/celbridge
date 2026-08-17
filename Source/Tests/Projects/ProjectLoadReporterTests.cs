using System.Text.Json;
using Celbridge.FileSystem.Services;
using Celbridge.Packages;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Reports;
using Celbridge.Resources;
using Celbridge.Tests.Migration.TestHelpers;
using Celbridge.Utilities;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Unit tests for ProjectLoadReporter — the stateful singleton that accumulates
/// project-load events from ProjectLoader plus the registry's sidecar snapshot, and writes one
/// report document on FlushAsync. The tests pin the resource key, the section layout, and the
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
        var reportWriter = new ReportWriter(_fileSystem, MigrationTestHelper.CreateMockLogger<ReportWriter>());
        _reporter = new ProjectLoadReporter(reportWriter, MigrationTestHelper.CreateMockLogger<ProjectLoadReporter>());
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
    public async Task FlushAsync_LandsUnderTheLogsReportsRoot()
    {
        // The report is addressable as a document, so the key it returns is the contract
        // the health button and the notification are written against.
        _reporter.BeginLoad(_projectFilePath);
        _reporter.RecordLoadOutcome(loadSucceeded: true, loadResult: Result.Ok());

        var reportSummary = await _reporter.FlushAsync();

        reportSummary.Should().NotBeNull();

        var key = reportSummary!.Resource.ToString();
        key.Should().StartWith("logs:reports/project-load-");
        key.Should().EndWith(".report");

        File.Exists(ResolveReportFilePath(reportSummary.Resource)).Should().BeTrue();
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
    public async Task FlushAsync_CleanLoad_WritesSummaryOnlyAndReportsHealthy()
    {
        // The healthy report is not empty: it carries the summary facts, which is what
        // makes the health button worth pressing when nothing is wrong.
        _reporter.BeginLoad(_projectFilePath);
        _reporter.RecordMigrationResult(
            MigrationResult.WithVersions(MigrationStatus.Complete, Result.Ok(), "0.2.7", "1.0.0"),
            userConfirmedUpgrade: false,
            userCancelledUpgrade: false);
        _reporter.RecordResourceCounts(fileCount: 412, folderCount: 37);
        _reporter.RecordLoadOutcome(loadSucceeded: true, loadResult: Result.Ok());

        var report = await FlushAndReadAsync();

        report.RootElement.GetProperty("version").GetInt32().Should().Be(ReportDocument.CurrentVersion);
        report.RootElement.GetProperty("id").GetString().Should().Be(ProjectLoadReporter.ReportId);
        report.RootElement.GetProperty("severity").GetString().Should().Be("info");
        report.RootElement.GetProperty("summary").GetString().Should().Contain("no issues");

        var sectionTitles = GetSectionTitles(report);
        sectionTitles.Should().Equal("Summary");

        var facts = GetSectionFacts(report, "Summary");
        facts["Resources"].Should().Be("412 files in 37 folders");
        facts["Project version"].Should().Be("0.2.7");
        facts["Application version"].Should().Be("1.0.0");
        facts["Migration status"].Should().Be("Complete");
        facts["Outcome"].Should().Be("Loaded");
    }

    [Test]
    public async Task FlushAsync_FailedLoad_IncludesErrorChainAndReportsError()
    {
        var migrationFailure = Result.Fail("Failed to parse project TOML file: (1,12) : error : Invalid \\r not followed by \\n");
        var loadFailure = Result.Fail("Failed to load project: 'test.celbridge'").WithErrors(migrationFailure);

        _reporter.BeginLoad(_projectFilePath);
        _reporter.RecordMigrationResult(
            MigrationResult.FromStatus(MigrationStatus.InvalidConfig, migrationFailure),
            userConfirmedUpgrade: false,
            userCancelledUpgrade: false);
        _reporter.RecordLoadOutcome(loadSucceeded: false, loadResult: loadFailure);

        var report = await FlushAndReadAsync();

        report.RootElement.GetProperty("severity").GetString().Should().Be("error");
        GetSectionFacts(report, "Summary")["Outcome"].Should().Be("Failed");

        var loadItems = GetSectionItems(report, "Load");
        loadItems.Should().HaveCount(2);

        GetSectionCodes(report, "Load").Should().Equal(
            ReportFindingCatalog.Project.MigrationFailed.Code,
            ReportFindingCatalog.Project.LoadFailed.Code);

        var detail = string.Join("\n", loadItems.Select(item => item.GetProperty("detail").GetString()));
        detail.Should().Contain("Invalid \\r not followed by \\n");
        detail.Should().Contain("Failed to load project");
    }

    [Test]
    public async Task FlushAsync_UserCancelledUpgrade_NotesCancellation()
    {
        _reporter.BeginLoad(_projectFilePath);
        _reporter.RecordMigrationResult(
            MigrationResult.WithVersions(MigrationStatus.UpgradeRequired, Result.Ok(), "0.2.7", "1.0.0"),
            userConfirmedUpgrade: false,
            userCancelledUpgrade: true);
        _reporter.RecordLoadOutcome(loadSucceeded: false, loadResult: null);

        var report = await FlushAndReadAsync();

        GetSectionCodes(report, "Load")
            .Should().Contain(ReportFindingCatalog.Project.UpgradeCancelled.Code);
    }

    [Test]
    public async Task FlushAsync_AfterRecordSidecarReport_IncludesFindingsWithResourcesAndActions()
    {
        // Mirrors the runtime flow: ProjectLoader pushes load info first, then WorkspaceLoader pushes
        // the registry's sidecar snapshot once the resources are populated.
        _reporter.BeginLoad(_projectFilePath);
        _reporter.RecordMigrationResult(
            MigrationResult.WithVersions(MigrationStatus.Complete, Result.Ok(), "1.0.0", "1.0.0"),
            userConfirmedUpgrade: false,
            userCancelledUpgrade: false);
        _reporter.RecordLoadOutcome(loadSucceeded: true, loadResult: Result.Ok());

        var sidecarReport = new SidecarReport(
            Healthy: Array.Empty<ResourceKey>(),
            Broken: new[] { new ResourceKey("bad.png.cel") },
            Orphan: new[] { new ResourceKey("foo.png.cel") });
        _reporter.RecordSidecarReport(sidecarReport);

        var report = await FlushAndReadAsync();

        report.RootElement.GetProperty("severity").GetString().Should().Be("warning");
        report.RootElement.GetProperty("summary").GetString().Should().Contain("2 issues");

        var items = GetSectionItems(report, "Sidecar files");
        items.Should().HaveCount(2);

        GetSectionCodes(report, "Sidecar files").Should().Equal(
            ReportFindingCatalog.Resource.OrphanSidecar.Code,
            ReportFindingCatalog.Resource.BrokenSidecar.Code);

        var orphan = items[0];
        orphan.GetProperty("resource").GetString().Should().Be("project:foo.png.cel");

        // The click-through is the point of the structured format: the finding names the
        // resource to open, rather than printing a path for the user to go and find.
        var action = orphan.GetProperty("actions")[0];
        action.GetProperty("kind").GetString().Should().Be("openResource");
        action.GetProperty("resource").GetString().Should().Be("project:foo.png.cel");

        items[1].GetProperty("resource").GetString().Should().Be("project:bad.png.cel");
    }

    [Test]
    public async Task FlushAsync_HealthySidecars_OmitsTheSidecarSection()
    {
        _reporter.BeginLoad(_projectFilePath);
        _reporter.RecordLoadOutcome(loadSucceeded: true, loadResult: Result.Ok());

        var sidecarReport = new SidecarReport(
            Healthy: new[] { new ResourceKey("foo.png.cel") },
            Broken: Array.Empty<ResourceKey>(),
            Orphan: Array.Empty<ResourceKey>());
        _reporter.RecordSidecarReport(sidecarReport);

        var report = await FlushAndReadAsync();

        GetSectionTitles(report).Should().NotContain("Sidecar files");
        report.RootElement.GetProperty("severity").GetString().Should().Be("info");
    }

    [Test]
    public async Task FlushAsync_AfterRecordConfigEntryErrors_IncludesTheSkippedEntries()
    {
        // The entries the config parser skipped reach a banner today; the report is where the reason
        // behind each one lands.
        _reporter.BeginLoad(_projectFilePath);
        _reporter.RecordLoadOutcome(loadSucceeded: true, loadResult: Result.Ok());

        var entryErrors = new[]
        {
            new ProjectConfigEntryError("contribution", "Unknown package 'acme-notes'")
        };
        _reporter.RecordConfigEntryErrors(entryErrors);

        var report = await FlushAndReadAsync();

        report.RootElement.GetProperty("severity").GetString().Should().Be("warning");

        var items = GetSectionItems(report, "Configuration (test.celbridge)");
        items.Should().ContainSingle();
        items[0].GetProperty("code").GetString().Should().Be(ReportFindingCatalog.Project.ConfigEntrySkipped.Code);
        items[0].GetProperty("message").GetString().Should().Contain("contribution");
        items[0].GetProperty("detail").GetString().Should().Contain("acme-notes");
    }

    [Test]
    public async Task FlushAsync_AfterRecordPackageReport_CountsInSummaryAndFailuresInSection()
    {
        _reporter.BeginLoad(_projectFilePath);
        _reporter.RecordLoadOutcome(loadSucceeded: true, loadResult: Result.Ok());

        var packageReport = new PackageDiscoveryReport
        {
            BundledPackageCount = 5,
            ProjectPackageCount = 1,
            ResolvedEditorCount = 3,
            Failures = new[]
            {
                new PackageLoadFailure
                {
                    Folder = @"C:\projects\demo\packages\excel-art",
                    PackageName = null,
                    Reason = PackageLoadFailureReason.InvalidManifest,
                    Detail = "Package has invalid 'name' value 'Excel Art'"
                },
                new PackageLoadFailure
                {
                    Folder = @"C:\projects\demo\packages\impostor",
                    PackageName = "celbridge.notes",
                    Reason = PackageLoadFailureReason.ReservedNamePrefix
                }
            }
        };
        _reporter.RecordPackageReport(packageReport);

        var report = await FlushAndReadAsync();

        var facts = GetSectionFacts(report, "Summary");
        facts["Packages loaded"].Should().Be("5 bundled, 1 project");
        facts["Editors resolved"].Should().Be("3");

        var items = GetSectionItems(report, "Packages");
        items.Should().HaveCount(2);
        items[0].GetProperty("code").GetString().Should().Be(ReportFindingCatalog.Package.PackageLoadFailed.Code);
        items[0].GetProperty("value").GetString().Should().Be("InvalidManifest");
        items[0].GetProperty("detail").GetString().Should().Contain("Excel Art");
        items[1].GetProperty("message").GetString().Should().Contain("celbridge.notes");

        report.RootElement.GetProperty("severity").GetString().Should().Be("error");
    }

    [Test]
    public async Task FlushAsync_CleanPackageDiscovery_OmitsThePackagesSection()
    {
        _reporter.BeginLoad(_projectFilePath);
        _reporter.RecordLoadOutcome(loadSucceeded: true, loadResult: Result.Ok());

        var packageReport = new PackageDiscoveryReport
        {
            BundledPackageCount = 5,
            ProjectPackageCount = 2,
            Failures = Array.Empty<PackageLoadFailure>()
        };
        _reporter.RecordPackageReport(packageReport);

        var report = await FlushAndReadAsync();

        GetSectionTitles(report).Should().NotContain("Packages");
        GetSectionFacts(report, "Summary")["Packages loaded"].Should().Be("5 bundled, 2 project");
    }

    [Test]
    public async Task FlushAsync_EditorFailures_SeparatesSkippedFromDegraded()
    {
        _reporter.BeginLoad(_projectFilePath);
        _reporter.RecordLoadOutcome(loadSucceeded: true, loadResult: Result.Ok());

        var packageReport = new PackageDiscoveryReport
        {
            BundledPackageCount = 5,
            ProjectPackageCount = 1,
            ResolvedEditorCount = 2,
            ResolvedEditorFailures = new[]
            {
                new ResolvedEditorLoadFailure
                {
                    EditorId = "notepad",
                    Detail = "Unknown package 'acme-notes'"
                }
            },
            ResolvedEditorWarnings = new[]
            {
                new ResolvedEditorLoadFailure
                {
                    EditorId = "charts",
                    Detail = "Config key 'theme' has an unsupported value shape"
                }
            }
        };
        _reporter.RecordPackageReport(packageReport);

        var report = await FlushAndReadAsync();

        var items = GetSectionItems(report, "Packages");
        items.Should().HaveCount(2);

        // The severity a finding carries comes from its descriptor, so the two kinds separate by code.
        items[0].GetProperty("code").GetString().Should().Be(ReportFindingCatalog.Package.EditorSkipped.Code);
        items[0].GetProperty("severity").GetString().Should().Be("error");
        items[0].GetProperty("message").GetString().Should().Contain("notepad");

        items[1].GetProperty("code").GetString().Should().Be(ReportFindingCatalog.Package.EditorDegraded.Code);
        items[1].GetProperty("severity").GetString().Should().Be("warning");
        items[1].GetProperty("message").GetString().Should().Contain("charts");
    }

    [Test]
    public async Task BeginLoad_ClearsPriorSidecarAndPackageState()
    {
        // A new project load invalidates the previous run's state. Each flush writes a
        // fresh report rather than carrying findings forward.
        _reporter.BeginLoad(_projectFilePath);
        _reporter.RecordSidecarReport(new SidecarReport(
            Healthy: Array.Empty<ResourceKey>(),
            Broken: Array.Empty<ResourceKey>(),
            Orphan: new[] { new ResourceKey("stale.png.cel") }));
        _reporter.RecordPackageReport(new PackageDiscoveryReport
        {
            BundledPackageCount = 5,
            ProjectPackageCount = 0,
            Failures = Array.Empty<PackageLoadFailure>()
        });
        _reporter.RecordResourceCounts(fileCount: 9, folderCount: 2);
        _reporter.RecordLoadOutcome(loadSucceeded: true, loadResult: Result.Ok());
        await _reporter.FlushAsync();

        _reporter.BeginLoad(_projectFilePath);
        _reporter.RecordMigrationResult(
            MigrationResult.WithVersions(MigrationStatus.Complete, Result.Ok(), "1.0.0", "1.0.0"),
            userConfirmedUpgrade: false,
            userCancelledUpgrade: false);
        _reporter.RecordLoadOutcome(loadSucceeded: true, loadResult: Result.Ok());

        var report = await FlushAndReadAsync();

        GetSectionTitles(report).Should().Equal("Summary");

        var facts = GetSectionFacts(report, "Summary");
        facts.Should().NotContainKey("Resources");
        facts.Should().NotContainKey("Packages loaded");
    }

    [Test]
    public async Task FlushAsync_CreatesReportsFolderWhenMissing()
    {
        Directory.Exists(Path.Combine(_projectFolderPath, ".celbridge", "logs", "reports")).Should().BeFalse();

        _reporter.BeginLoad(_projectFilePath);
        _reporter.RecordMigrationResult(
            MigrationResult.FromStatus(MigrationStatus.Failed, Result.Fail("boom")),
            userConfirmedUpgrade: false,
            userCancelledUpgrade: false);
        _reporter.RecordLoadOutcome(loadSucceeded: false, loadResult: null);

        var reportSummary = await _reporter.FlushAsync();

        reportSummary.Should().NotBeNull();
        File.Exists(ResolveReportFilePath(reportSummary!.Resource)).Should().BeTrue();
    }

    private async Task<JsonDocument> FlushAndReadAsync()
    {
        var reportSummary = await _reporter.FlushAsync();
        reportSummary.Should().NotBeNull();

        var content = await File.ReadAllTextAsync(ResolveReportFilePath(reportSummary!.Resource));

        return JsonDocument.Parse(content);
    }

    private string ResolveReportFilePath(ResourceKey reportResource)
    {
        var reportFileName = Path.GetFileName(reportResource.ToString());

        return Path.Combine(_projectFolderPath, ".celbridge", "logs", "reports", reportFileName);
    }

    private static List<string?> GetSectionTitles(JsonDocument report)
    {
        return report.RootElement.GetProperty("sections")
            .EnumerateArray()
            .Select(section => section.GetProperty("title").GetString())
            .ToList();
    }

    private static List<JsonElement> GetSectionItems(JsonDocument report, string sectionTitle)
    {
        var section = report.RootElement.GetProperty("sections")
            .EnumerateArray()
            .Single(candidate => candidate.GetProperty("title").GetString() == sectionTitle);

        return section.GetProperty("items").EnumerateArray().ToList();
    }

    private static List<string?> GetSectionCodes(JsonDocument report, string sectionTitle)
    {
        return GetSectionItems(report, sectionTitle)
            .Select(item => item.GetProperty("code").GetString())
            .ToList();
    }

    private static Dictionary<string, string?> GetSectionFacts(JsonDocument report, string sectionTitle)
    {
        var facts = new Dictionary<string, string?>();
        foreach (var item in GetSectionItems(report, sectionTitle))
        {
            var label = item.GetProperty("message").GetString();
            if (label is null)
            {
                continue;
            }

            var value = item.TryGetProperty("value", out var valueElement)
                ? valueElement.GetString()
                : null;

            facts[label] = value;
        }

        return facts;
    }
}
