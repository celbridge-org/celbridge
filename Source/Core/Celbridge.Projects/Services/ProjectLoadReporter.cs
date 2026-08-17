using Celbridge.Logging;
using Celbridge.Packages;
using Celbridge.Reports;
using Celbridge.Resources;
using Celbridge.Utilities;

namespace Celbridge.Projects.Services;

/// <summary>
/// In-memory accumulator of project-load state, written out as a report document on flush.
/// </summary>
public sealed class ProjectLoadReporter : IProjectLoadReporter
{
    /// <summary>
    /// Identifies every report this reporter writes.
    /// </summary>
    public const string ReportId = "project-load";

    // A project with pathological numbers of findings would otherwise produce a report too large to
    // read or open.
    private readonly IReportWriter _reportWriter;
    private readonly ILogger<ProjectLoadReporter> _logger;

    private string _projectFilePath = string.Empty;
    private DateTimeOffset? _loadStartedAt;
    private DateTimeOffset? _loadCompletedAt;
    private MigrationResult? _migrationResult;
    private bool _userConfirmedUpgrade;
    private bool _userCancelledUpgrade;
    private bool _loadSucceeded;
    private Result? _loadResult;
    private IReadOnlyList<ProjectConfigEntryError> _configEntryErrors = Array.Empty<ProjectConfigEntryError>();
    private PackageDiscoveryReport? _packageReport;
    private SidecarReport? _sidecarReport;
    private int? _fileResourceCount;
    private int? _folderResourceCount;

    public ProjectLoadReporter(
        IReportWriter reportWriter,
        ILogger<ProjectLoadReporter> logger)
    {
        _reportWriter = reportWriter;
        _logger = logger;
    }

    public void BeginLoad(string projectFilePath)
    {
        _projectFilePath = projectFilePath;
        _loadStartedAt = DateTimeOffset.UtcNow;
        _loadCompletedAt = null;
        _migrationResult = null;
        _userConfirmedUpgrade = false;
        _userCancelledUpgrade = false;
        _loadSucceeded = false;
        _loadResult = null;
        _configEntryErrors = Array.Empty<ProjectConfigEntryError>();
        _packageReport = null;
        _sidecarReport = null;
        _fileResourceCount = null;
        _folderResourceCount = null;
    }

    public void RecordMigrationResult(MigrationResult result, bool userConfirmedUpgrade, bool userCancelledUpgrade)
    {
        _migrationResult = result;
        _userConfirmedUpgrade = userConfirmedUpgrade;
        _userCancelledUpgrade = userCancelledUpgrade;
    }

    public void RecordLoadOutcome(bool loadSucceeded, Result? loadResult)
    {
        _loadSucceeded = loadSucceeded;
        _loadResult = loadResult;
        _loadCompletedAt = DateTimeOffset.UtcNow;
    }

    public void RecordConfigEntryErrors(IReadOnlyList<ProjectConfigEntryError> entryErrors)
    {
        _configEntryErrors = entryErrors;
    }

    public void RecordPackageReport(PackageDiscoveryReport report)
    {
        _packageReport = report;
    }

    public void RecordResourceCounts(int fileCount, int folderCount)
    {
        _fileResourceCount = fileCount;
        _folderResourceCount = folderCount;
    }

    public void RecordSidecarReport(SidecarReport report)
    {
        _sidecarReport = report;
    }

    public async Task<ProjectLoadReportSummary?> FlushAsync()
    {
        if (string.IsNullOrEmpty(_projectFilePath))
        {
            return null;
        }

        try
        {
            var report = BuildReport();

            var writeResult = await ReportLocation.WriteReportAsync(_reportWriter, report, _projectFilePath);
            if (writeResult.IsFailure)
            {
                _logger.LogWarning(writeResult, $"Failed to write project load report for: '{_projectFilePath}'");
                return null;
            }

            var reportResource = writeResult.Value;
            var issueCount = CountIssues(report.Sections);

            return new ProjectLoadReportSummary(reportResource, report.Severity, issueCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to write project load report for: '{_projectFilePath}'");
            return null;
        }
    }

    private ReportDocument BuildReport()
    {
        var sections = new List<ReportSection>
        {
            BuildSummarySection()
        };

        var loadIssuesSection = BuildLoadIssuesSection();
        if (loadIssuesSection is not null)
        {
            sections.Add(loadIssuesSection);
        }

        var configurationSection = BuildConfigurationSection();
        if (configurationSection is not null)
        {
            sections.Add(configurationSection);
        }

        var packagesSection = BuildPackagesSection();
        if (packagesSection is not null)
        {
            sections.Add(packagesSection);
        }

        var sidecarSection = BuildSidecarSection();
        if (sidecarSection is not null)
        {
            sections.Add(sidecarSection);
        }

        var severity = ResolveWorstSeverity(sections);
        var summary = ComposeSummaryLine(severity, sections);

        // Stamped with the start of the load rather than the moment of writing, so every flush during a
        // load addresses the same file and one load leaves one report behind.
        var generatedAt = _loadStartedAt ?? DateTimeOffset.UtcNow;

        return new ReportDocument(
            ReportId,
            "Project Load",
            generatedAt,
            severity,
            summary,
            sections);
    }

    private ReportSection BuildSummarySection()
    {
        var items = new List<ReportItem>();

        var projectName = Path.GetFileNameWithoutExtension(_projectFilePath);
        items.Add(CreateFact("Project", projectName));

        if (_fileResourceCount is int fileCount
            && _folderResourceCount is int folderCount)
        {
            items.Add(CreateFact("Resources", $"{fileCount} files in {folderCount} folders"));
        }

        if (_packageReport is PackageDiscoveryReport packageReport)
        {
            var packageCounts = $"{packageReport.BundledPackageCount} bundled, {packageReport.ProjectPackageCount} project";
            items.Add(CreateFact("Packages loaded", packageCounts));
            items.Add(CreateFact("Editors resolved", packageReport.ResolvedEditorCount.ToString()));
        }

        if (_migrationResult is MigrationResult migrationResult)
        {
            if (!string.IsNullOrEmpty(migrationResult.OldVersion))
            {
                items.Add(CreateFact("Project version", migrationResult.OldVersion));
            }
            if (!string.IsNullOrEmpty(migrationResult.NewVersion))
            {
                items.Add(CreateFact("Application version", migrationResult.NewVersion));
            }

            items.Add(CreateFact("Migration status", migrationResult.Status.ToString()));
        }

        items.Add(CreateFact("Outcome", ResolveOutcomeText()));

        if (_loadCompletedAt is DateTimeOffset completedAt
            && _loadStartedAt is DateTimeOffset startedAt)
        {
            var durationMilliseconds = (completedAt - startedAt).TotalMilliseconds;
            items.Add(CreateFact("Load duration", $"{durationMilliseconds:F0} ms"));
        }

        return new ReportSection("Summary", ReportSectionKind.Facts, ReportSeverity.Info, items);
    }

    private string ResolveOutcomeText()
    {
        if (_loadCompletedAt is null)
        {
            return "In progress";
        }

        return _loadSucceeded ? "Loaded" : "Failed";
    }

    private ReportSection? BuildLoadIssuesSection()
    {
        var items = new List<ReportItem>();

        if (_migrationResult is null)
        {
            // Migration is recorded on every path that reaches loading, so its absence only says
            // something when the load finished without succeeding.
            if (_loadCompletedAt is null
                || _loadSucceeded)
            {
                return null;
            }

            items.Add(ReportFinding.Create(ReportFindingCatalog.Project.MigrationNotReached));

            return new ReportSection("Load", ReportSectionKind.Findings, ReportSeverity.Error, items);
        }

        if (_userCancelledUpgrade)
        {
            items.Add(ReportFinding.Create(ReportFindingCatalog.Project.UpgradeCancelled));
        }

        if (_migrationResult.OperationResult.IsFailure)
        {
            items.Add(CreateResultItem(ReportFindingCatalog.Project.MigrationFailed, _migrationResult.OperationResult));
        }

        if (_loadResult is { IsFailure: true } loadResult)
        {
            items.Add(CreateResultItem(ReportFindingCatalog.Project.LoadFailed, loadResult));
        }

        if (items.Count == 0)
        {
            return null;
        }

        return new ReportSection("Load", ReportSectionKind.Findings, ResolveWorstItemSeverity(items), items);
    }

    private ReportSection? BuildConfigurationSection()
    {
        if (_configEntryErrors.Count == 0)
        {
            return null;
        }

        var projectFileName = Path.GetFileName(_projectFilePath);

        var items = new List<ReportItem>();
        foreach (var entryError in _configEntryErrors)
        {
            var item = ReportFinding.Create(ReportFindingCatalog.Project.ConfigEntrySkipped, entryError.EntryName) with
            {
                Detail = NormaliseDetail(entryError.Message)
            };

            items.Add(item);
        }

        return new ReportSection($"Configuration ({projectFileName})", ReportSectionKind.Findings, ResolveWorstItemSeverity(items), items);
    }

    private ReportSection? BuildPackagesSection()
    {
        if (_packageReport is null)
        {
            return null;
        }

        var report = _packageReport;
        var items = new List<ReportItem>();

        foreach (var failure in report.Failures)
        {
            var location = string.IsNullOrEmpty(failure.PackageName)
                ? failure.Folder
                : $"{failure.PackageName} ({failure.Folder})";

            var item = ReportFinding.Create(ReportFindingCatalog.Package.PackageLoadFailed, location) with
            {
                Value = failure.Reason.ToString(),
                Detail = NormaliseDetail(failure.Detail)
            };

            items.Add(item);
        }

        foreach (var failure in report.ResolvedEditorFailures)
        {
            var item = ReportFinding.Create(ReportFindingCatalog.Package.EditorSkipped, failure.EditorId) with
            {
                Detail = NormaliseDetail(failure.Detail)
            };

            items.Add(item);
        }

        foreach (var warning in report.ResolvedEditorWarnings)
        {
            var item = ReportFinding.Create(ReportFindingCatalog.Package.EditorDegraded, warning.EditorId) with
            {
                Detail = NormaliseDetail(warning.Detail)
            };

            items.Add(item);
        }

        if (items.Count == 0)
        {
            return null;
        }

        return new ReportSection("Packages", ReportSectionKind.Findings, ResolveWorstItemSeverity(items), items);
    }

    private ReportSection? BuildSidecarSection()
    {
        if (_sidecarReport is null)
        {
            return null;
        }

        var report = _sidecarReport;
        var items = new List<ReportItem>();

        var orphanFiles = report.Orphan
            .OrderBy(resource => resource.ToString(), StringComparer.Ordinal)
            .ToList();

        foreach (var orphanFile in orphanFiles)
        {
            var item = ReportFinding.Create(ReportFindingCatalog.Resource.OrphanSidecar) with
            {
                Resource = orphanFile,
                Actions = CreateOpenResourceActions(orphanFile)
            };

            items.Add(item);
        }

        var brokenFiles = report.Broken
            .OrderBy(resource => resource.ToString(), StringComparer.Ordinal)
            .ToList();

        foreach (var brokenFile in brokenFiles)
        {
            var item = ReportFinding.Create(ReportFindingCatalog.Resource.BrokenSidecar) with
            {
                Resource = brokenFile,
                Actions = CreateOpenResourceActions(brokenFile)
            };

            items.Add(item);
        }

        if (items.Count == 0)
        {
            return null;
        }

        return new ReportSection("Sidecar files", ReportSectionKind.Findings, ResolveWorstItemSeverity(items), items);
    }

    private static IReadOnlyList<ReportAction> CreateOpenResourceActions(ResourceKey resource)
    {
        var action = new ReportAction(ReportActionKind.OpenResource, $"Open {resource.ResourceName}")
        {
            Resource = resource
        };

        return new List<ReportAction>
        {
            action
        };
    }

    private static ReportItem CreateFact(string label, string value)
    {
        return new ReportItem(ReportSeverity.Info, label)
        {
            Value = value
        };
    }

    private static ReportItem CreateResultItem(ReportFindingDescriptor descriptor, Result result)
    {
        var detail = result.MessageChain;
        if (string.IsNullOrEmpty(detail))
        {
            detail = result.DiagnosticReport;
        }

        return ReportFinding.Create(descriptor) with
        {
            Detail = NormaliseDetail(detail)
        };
    }

    private static string ComposeSummaryLine(ReportSeverity severity, IReadOnlyList<ReportSection> sections)
    {
        if (severity == ReportSeverity.Info)
        {
            return "The project loaded with no issues.";
        }

        var issueCount = CountIssues(sections);
        var issueLabel = issueCount == 1 ? "issue" : "issues";

        return $"The project loaded with {issueCount} {issueLabel}.";
    }

    private static int CountIssues(IReadOnlyList<ReportSection> sections)
    {
        return sections
            .SelectMany(section => section.Items)
            .Count(item => item.Severity != ReportSeverity.Info);
    }

    private static ReportSeverity ResolveWorstSeverity(IReadOnlyList<ReportSection> sections)
    {
        var worst = ReportSeverity.Info;
        foreach (var section in sections)
        {
            if (section.Severity > worst)
            {
                worst = section.Severity;
            }
        }

        return worst;
    }

    private static ReportSeverity ResolveWorstItemSeverity(IReadOnlyList<ReportItem> items)
    {
        var worst = ReportSeverity.Info;
        foreach (var item in items)
        {
            if (item.Severity > worst)
            {
                worst = item.Severity;
            }
        }

        return worst;
    }

    private static string? NormaliseDetail(string? detail)
    {
        if (string.IsNullOrEmpty(detail))
        {
            return null;
        }

        return detail.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd();
    }
}
