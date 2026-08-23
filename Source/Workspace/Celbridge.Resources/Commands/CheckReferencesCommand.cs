using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Localization;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Reports;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Scans the project's text files for project: references that do not resolve, optionally writing the
/// findings as a report document and opening it.
/// </summary>
public sealed class CheckReferencesCommand : CommandBase, ICheckReferencesCommand
{
    /// <summary>
    /// Identifies every report this command writes.
    /// </summary>
    public const string ReportId = "check-references";

    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectService _projectService;
    private readonly ICommandService _commandService;
    private readonly IReportWriter _reportWriter;
    private readonly ILogger<CheckReferencesCommand> _logger;
    private readonly ILocalizerService _localizerService;

    public CheckReferencesCommand(
        IWorkspaceWrapper workspaceWrapper,
        IProjectService projectService,
        ICommandService commandService,
        IReportWriter reportWriter,
        ILogger<CheckReferencesCommand> logger,
        ILocalizerService localizerService)
    {
        _workspaceWrapper = workspaceWrapper;
        _projectService = projectService;
        _commandService = commandService;
        _reportWriter = reportWriter;
        _logger = logger;
        _localizerService = localizerService;
    }

    public bool OpenReport { get; set; }

    public CheckReferencesReport ResultValue { get; private set; } = new CheckReferencesReport(
        BrokenReferences: Array.Empty<BrokenReference>(),
        CheckedTargetCount: 0);

    public override async Task<Result> ExecuteAsync()
    {
        var workspaceService = _workspaceWrapper.WorkspaceService;
        var registry = workspaceService.ResourceService.Registry;
        var scanner = workspaceService.ResourceService.Scanner;

        // One walk covers every target; a per-target query re-reads the whole project each time.
        var referenceIndex = await scanner.BuildReferenceIndexAsync();

        var checkedTargetCount = 0;
        var brokenReferences = new List<BrokenReference>();

        foreach (var target in referenceIndex.ReferencedTargets)
        {
            checkedTargetCount++;

            var resourceResult = registry.GetResource(target);
            if (resourceResult.IsSuccess)
            {
                continue;
            }
            foreach (var site in referenceIndex.GetReferencers(target))
            {
                brokenReferences.Add(new BrokenReference(site, target));
            }
        }

        // Deterministic ordering so test assertions and human readers see the
        // same shape every time.
        brokenReferences.Sort((a, b) =>
        {
            var byTarget = string.Compare(a.MissingTarget.ToString(), b.MissingTarget.ToString(), StringComparison.Ordinal);
            if (byTarget != 0)
            {
                return byTarget;
            }

            var bySource = string.Compare(a.Source.ToString(), b.Source.ToString(), StringComparison.Ordinal);
            if (bySource != 0)
            {
                return bySource;
            }

            var byLine = a.Site.Line.CompareTo(b.Site.Line);
            if (byLine != 0)
            {
                return byLine;
            }

            return a.Site.Column.CompareTo(b.Site.Column);
        });

        ResultValue = new CheckReferencesReport(brokenReferences, checkedTargetCount);

        if (OpenReport)
        {
            await WriteAndOpenReportAsync(ResultValue);
        }

        return Result.Ok();
    }

    // The check itself succeeded whatever happens here, so a report that could not be written is logged
    // rather than failing the command.
    private async Task WriteAndOpenReportAsync(CheckReferencesReport checkReport)
    {
        var currentProject = _projectService.CurrentProject;
        if (currentProject is null)
        {
            return;
        }

        var report = BuildReport(checkReport);

        var writeResult = await ReportLocation.WriteReportAsync(_reportWriter, report, currentProject.ProjectDataFolderPath);
        if (writeResult.IsFailure)
        {
            _logger.LogWarning(writeResult, "Failed to write the check references report.");
            return;
        }

        var reportResource = writeResult.Value;

        // Fire and forget: this runs inside the command queue, so awaiting an enqueued command here would
        // deadlock it.
        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = reportResource;

            // Every run writes the same resource, so a report left open from an earlier run is already
            // on screen showing the previous result. The logs: root is deliberately unwatched, so the
            // reload is asked for here rather than arriving as a file change event.
            command.ForceReload = true;
        });
    }

    private ReportDocument BuildReport(CheckReferencesReport checkReport)
    {
        var summarySection = BuildSummarySection(checkReport);

        var sections = new List<ReportSection>
        {
            summarySection
        };

        var findingsSection = BuildFindingsSection(checkReport);
        if (findingsSection is not null)
        {
            sections.Add(findingsSection);
        }

        var severity = findingsSection?.Severity ?? ReportSeverity.Info;
        var summary = ComposeSummaryLine(checkReport);

        return new ReportDocument(
            ReportId,
            _localizerService.GetString("Report_CheckReferences_Title"),
            DateTimeOffset.UtcNow,
            severity,
            summary,
            sections);
    }

    private ReportSection BuildSummarySection(CheckReferencesReport checkReport)
    {
        // The scan walks referenced resources, and each one can be named by any number of references,
        // so the counts are labelled by what they actually count.
        var items = new List<ReportItem>
        {
            CreateFact("Report_CheckReferences_Fact_ReferencedResources", checkReport.CheckedTargetCount.ToString()),
            CreateFact("Report_CheckReferences_Fact_MissingResources", CountMissingTargets(checkReport).ToString()),
            CreateFact("Report_CheckReferences_Fact_BrokenReferences", checkReport.BrokenReferences.Count.ToString())
        };

        var title = _localizerService.GetString("Report_CheckReferences_Section_Summary");

        return new ReportSection(title, ReportSectionKind.Facts, ReportSeverity.Info, items);
    }

    private ReportSection? BuildFindingsSection(CheckReferencesReport checkReport)
    {
        if (checkReport.BrokenReferences.Count == 0)
        {
            return null;
        }

        var items = new List<ReportItem>();
        foreach (var brokenReference in checkReport.BrokenReferences)
        {
            var site = brokenReference.Site;
            var location = new ReportSourceLocation(site.Line, site.Column);

            var label = _localizerService.GetString("Report_Action_OpenResource", site.Source.ResourceName);

            var action = new ReportAction(ReportActionKind.OpenResource, label)
            {
                Resource = site.Source,
                Location = location
            };

            var actions = new List<ReportAction>
            {
                action
            };

            // No detail: every occurrence says the same thing as the descriptor's message, and what a
            // lexical scan can and cannot tell apart belongs to the finding kind rather than to each
            // place it was found.
            var item = ReportFinding.Create(_localizerService, ReportFindingCatalog.Resource.MissingReference) with
            {
                Resource = site.Source,
                Target = brokenReference.MissingTarget,
                Actions = actions
            };

            items.Add(item);
        }

        var title = _localizerService.GetString("Report_CheckReferences_Section_MissingReferences");

        return new ReportSection(title, ReportSectionKind.Findings, ReportSeverity.Warning, items);
    }

    private ReportItem CreateFact(string labelKey, string value)
    {
        return new ReportItem(ReportSeverity.Info, _localizerService.GetString(labelKey))
        {
            Value = value
        };
    }

    private string ComposeSummaryLine(CheckReferencesReport checkReport)
    {
        var checkedCount = checkReport.CheckedTargetCount;
        if (checkedCount == 0)
        {
            return _localizerService.GetString("Report_CheckReferences_Summary_NothingToCheck");
        }

        var brokenCount = checkReport.BrokenReferences.Count;
        if (brokenCount == 0)
        {
            var allFoundKey = checkedCount == 1
                ? "Report_CheckReferences_Summary_AllFound_One"
                : "Report_CheckReferences_Summary_AllFound_Many";

            return _localizerService.GetString(allFoundKey, checkedCount);
        }

        // Both counts vary, and a language can inflect either, so each combination is its own sentence
        // rather than a noun picked per count and dropped into a shared one.
        var missingCount = CountMissingTargets(checkReport);
        var brokenKey = ResolveBrokenSummaryKey(brokenCount, missingCount);

        return _localizerService.GetString(brokenKey, brokenCount, missingCount);
    }

    private static string ResolveBrokenSummaryKey(int brokenCount, int missingCount)
    {
        if (brokenCount == 1)
        {
            return missingCount == 1
                ? "Report_CheckReferences_Summary_Broken_OneRef_OneResource"
                : "Report_CheckReferences_Summary_Broken_OneRef_ManyResources";
        }

        return missingCount == 1
            ? "Report_CheckReferences_Summary_Broken_ManyRefs_OneResource"
            : "Report_CheckReferences_Summary_Broken_ManyRefs_ManyResources";
    }

    // A missing resource is usually named by more than one reference, so the two counts differ.
    private static int CountMissingTargets(CheckReferencesReport checkReport)
    {
        var missingTargets = new HashSet<ResourceKey>();
        foreach (var brokenReference in checkReport.BrokenReferences)
        {
            missingTargets.Add(brokenReference.MissingTarget);
        }

        return missingTargets.Count;
    }
}
