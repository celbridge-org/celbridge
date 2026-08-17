using Celbridge.Logging;
using Celbridge.Packages;
using Celbridge.Reports;
using Celbridge.Resources;

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

    /// <summary>
    /// Sub-folder of the logs root that reports are written to.
    /// </summary>
    public const string ReportsFolderName = "reports";

    // A project with pathological numbers of findings would otherwise produce a report too large to
    // read or open.
    private const int MaxItemsPerSection = 200;

    // The logs: root name, which the resource layer owns. Repeated here because it sits above this
    // project and cannot be referenced from it.
    private const string LogsRootName = "logs";

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
    private PackageDiscoveryReport? _packageReport;
    private ProjectCheckReport? _checkReport;
    private DateTimeOffset? _checkCompletedAt;
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
        _packageReport = null;
        _checkReport = null;
        _checkCompletedAt = null;
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

    public void RecordPackageReport(PackageDiscoveryReport report)
    {
        _packageReport = report;
    }

    public void RecordResourceCounts(int fileCount, int folderCount)
    {
        _fileResourceCount = fileCount;
        _folderResourceCount = folderCount;
    }

    public void RecordCheckReport(ProjectCheckReport report)
    {
        _checkReport = report;
        _checkCompletedAt = DateTimeOffset.UtcNow;
    }

    public async Task<ResourceKey?> FlushAsync()
    {
        if (string.IsNullOrEmpty(_projectFilePath))
        {
            return null;
        }

        try
        {
            var report = BuildReport();
            var reportsFolderPath = ResolveReportsFolderPath(_projectFilePath);

            var writeResult = await _reportWriter.WriteReportAsync(report, reportsFolderPath);
            if (writeResult.IsFailure)
            {
                _logger.LogWarning(writeResult, $"Failed to write project load report for: '{_projectFilePath}'");
                return null;
            }

            var reportFilePath = writeResult.Value;
            var reportFileName = Path.GetFileName(reportFilePath);

            return new ResourceKey($"{LogsRootName}:{ReportsFolderName}/{reportFileName}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to write project load report for: '{_projectFilePath}'");
            return null;
        }
    }

    private static string ResolveReportsFolderPath(string projectFilePath)
    {
        var projectFolder = Path.GetDirectoryName(projectFilePath) ?? string.Empty;

        return Path.Combine(
            projectFolder,
            ProjectConstants.CelbridgeFolder,
            ProjectConstants.LogsFolder,
            ReportsFolderName);
    }

    private ReportDocument BuildReport()
    {
        var omittedItemCount = 0;

        var sections = new List<ReportSection>
        {
            BuildSummarySection()
        };

        var loadIssuesSection = BuildLoadIssuesSection();
        if (loadIssuesSection is not null)
        {
            sections.Add(loadIssuesSection);
        }

        var packagesSection = BuildPackagesSection(ref omittedItemCount);
        if (packagesSection is not null)
        {
            sections.Add(packagesSection);
        }

        var checkSection = BuildCheckSection(ref omittedItemCount);
        if (checkSection is not null)
        {
            sections.Add(checkSection);
        }

        var severity = ResolveWorstSeverity(sections);
        var summary = ComposeSummaryLine(severity, sections);

        var truncation = omittedItemCount > 0
            ? new ReportTruncation(omittedItemCount)
            : null;

        return new ReportDocument(
            ReportId,
            "Project Load",
            DateTimeOffset.UtcNow,
            severity,
            summary,
            sections)
        {
            Truncated = truncation
        };
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

        if (_checkCompletedAt is DateTimeOffset checkedAt)
        {
            items.Add(CreateFact("Consistency check", checkedAt.UtcDateTime.ToString("u")));
        }

        return new ReportSection("Summary", ReportSeverity.Info, items);
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

            items.Add(new ReportItem(ReportSeverity.Error, "Migration step was not reached."));

            return new ReportSection("Load", ReportSeverity.Error, items);
        }

        if (_userCancelledUpgrade)
        {
            items.Add(new ReportItem(ReportSeverity.Warning, "The upgrade was cancelled, so the project was not loaded."));
        }

        if (_migrationResult.OperationResult.IsFailure)
        {
            items.Add(CreateResultItem("Migration failed.", _migrationResult.OperationResult));
        }

        if (_loadResult is { IsFailure: true } loadResult)
        {
            items.Add(CreateResultItem("Project load failed.", loadResult));
        }

        if (items.Count == 0)
        {
            return null;
        }

        return new ReportSection("Load", ResolveWorstItemSeverity(items), items);
    }

    private ReportSection? BuildPackagesSection(ref int omittedItemCount)
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

            items.Add(new ReportItem(ReportSeverity.Error, $"Package failed to load: {location}")
            {
                Value = failure.Reason.ToString(),
                Detail = NormaliseDetail(failure.Detail)
            });
        }

        foreach (var failure in report.ResolvedEditorFailures)
        {
            items.Add(new ReportItem(ReportSeverity.Error, $"Editor skipped: {failure.EditorId}")
            {
                Detail = NormaliseDetail(failure.Detail)
            });
        }

        foreach (var warning in report.ResolvedEditorWarnings)
        {
            items.Add(new ReportItem(ReportSeverity.Warning, $"Editor degraded: {warning.EditorId}")
            {
                Detail = NormaliseDetail(warning.Detail)
            });
        }

        if (items.Count == 0)
        {
            return null;
        }

        var cappedItems = CapItems(items, ref omittedItemCount);

        return new ReportSection("Packages", ResolveWorstItemSeverity(cappedItems), cappedItems);
    }

    private ReportSection? BuildCheckSection(ref int omittedItemCount)
    {
        if (_checkReport is null)
        {
            return null;
        }

        var report = _checkReport;
        var items = new List<ReportItem>();

        foreach (var brokenReference in report.BrokenReferences)
        {
            items.Add(new ReportItem(ReportSeverity.Warning, "References a missing resource.")
            {
                Resource = brokenReference.Source,
                Target = brokenReference.MissingTarget,
                Detail = "The reference was left in place; the project loaded without it.",
                Actions = CreateOpenResourceActions(brokenReference.Source)
            });
        }

        foreach (var orphanFile in report.OrphanCelFiles)
        {
            items.Add(new ReportItem(ReportSeverity.Warning, "Orphan .cel file: no resource it describes.")
            {
                Resource = orphanFile,
                Actions = CreateOpenResourceActions(orphanFile)
            });
        }

        foreach (var brokenFile in report.BrokenCelFiles)
        {
            items.Add(new ReportItem(ReportSeverity.Warning, "Broken .cel file: could not be parsed.")
            {
                Resource = brokenFile,
                Actions = CreateOpenResourceActions(brokenFile)
            });
        }

        if (items.Count == 0)
        {
            return null;
        }

        var cappedItems = CapItems(items, ref omittedItemCount);

        return new ReportSection("Consistency check", ResolveWorstItemSeverity(cappedItems), cappedItems);
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

    private static ReportItem CreateResultItem(string message, Result result)
    {
        var detail = result.MessageChain;
        if (string.IsNullOrEmpty(detail))
        {
            detail = result.DiagnosticReport;
        }

        return new ReportItem(ReportSeverity.Error, message)
        {
            Detail = NormaliseDetail(detail)
        };
    }

    private static IReadOnlyList<ReportItem> CapItems(List<ReportItem> items, ref int omittedItemCount)
    {
        if (items.Count <= MaxItemsPerSection)
        {
            return items;
        }

        omittedItemCount += items.Count - MaxItemsPerSection;

        return items.Take(MaxItemsPerSection).ToList();
    }

    private static string ComposeSummaryLine(ReportSeverity severity, IReadOnlyList<ReportSection> sections)
    {
        if (severity == ReportSeverity.Info)
        {
            return "The project loaded with no issues.";
        }

        var issueCount = sections
            .SelectMany(section => section.Items)
            .Count(item => item.Severity != ReportSeverity.Info);

        var issueLabel = issueCount == 1 ? "issue" : "issues";

        return $"The project loaded with {issueCount} {issueLabel}.";
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
