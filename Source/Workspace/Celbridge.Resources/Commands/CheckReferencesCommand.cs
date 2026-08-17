using Celbridge.Commands;
using Celbridge.Documents;
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

    // A project with pathological numbers of findings would otherwise produce a report too large to
    // read or open.
    private const int MaxReportItems = 200;

    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectService _projectService;
    private readonly ICommandService _commandService;
    private readonly IReportWriter _reportWriter;
    private readonly ILogger<CheckReferencesCommand> _logger;

    public CheckReferencesCommand(
        IWorkspaceWrapper workspaceWrapper,
        IProjectService projectService,
        ICommandService commandService,
        IReportWriter reportWriter,
        ILogger<CheckReferencesCommand> logger)
    {
        _workspaceWrapper = workspaceWrapper;
        _projectService = projectService;
        _commandService = commandService;
        _reportWriter = reportWriter;
        _logger = logger;
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

        var writeResult = await ReportLocation.WriteReportAsync(_reportWriter, report, currentProject.ProjectFilePath);
        if (writeResult.IsFailure)
        {
            _logger.LogWarning(writeResult, "Failed to write the check references report.");
            return;
        }

        var reportResource = writeResult.Value;

        // Fire and forget: this runs inside the command queue, so awaiting an enqueued command here would
        // deadlock it.
        _commandService.Execute<IOpenDocumentCommand>(command => command.FileResource = reportResource);
    }

    private static ReportDocument BuildReport(CheckReferencesReport checkReport)
    {
        var summarySection = BuildSummarySection(checkReport);

        var sections = new List<ReportSection>
        {
            summarySection
        };

        var omittedItemCount = 0;

        var findingsSection = BuildFindingsSection(checkReport, ref omittedItemCount);
        if (findingsSection is not null)
        {
            sections.Add(findingsSection);
        }

        var severity = findingsSection?.Severity ?? ReportSeverity.Info;
        var summary = ComposeSummaryLine(checkReport);

        var truncation = omittedItemCount > 0
            ? new ReportTruncation(omittedItemCount)
            : null;

        return new ReportDocument(
            ReportId,
            "Check References",
            DateTimeOffset.UtcNow,
            severity,
            summary,
            sections)
        {
            Truncated = truncation
        };
    }

    private static ReportSection BuildSummarySection(CheckReferencesReport checkReport)
    {
        var items = new List<ReportItem>
        {
            CreateFact("References checked", checkReport.CheckedTargetCount.ToString()),
            CreateFact("References not found", checkReport.BrokenReferences.Count.ToString())
        };

        return new ReportSection("Summary", ReportSectionKind.Facts, ReportSeverity.Info, items);
    }

    private static ReportSection? BuildFindingsSection(CheckReferencesReport checkReport, ref int omittedItemCount)
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

            var action = new ReportAction(ReportActionKind.OpenResource, $"Open {site.Source.ResourceName}")
            {
                Resource = site.Source,
                Location = location
            };

            var actions = new List<ReportAction>
            {
                action
            };

            var item = ReportFinding.Create(ReportFindingCatalog.Resource.MissingReference) with
            {
                Resource = site.Source,
                Target = brokenReference.MissingTarget,
                Detail = "The scan matches reference literals in the file text, so a key quoted as an example reads the same as a live reference.",
                Actions = actions
            };

            items.Add(item);
        }

        var cappedItems = items;
        if (items.Count > MaxReportItems)
        {
            omittedItemCount += items.Count - MaxReportItems;
            cappedItems = items.Take(MaxReportItems).ToList();
        }

        return new ReportSection("Missing references", ReportSectionKind.Findings, ReportSeverity.Warning, cappedItems);
    }

    private static ReportItem CreateFact(string label, string value)
    {
        return new ReportItem(ReportSeverity.Info, label)
        {
            Value = value
        };
    }

    private static string ComposeSummaryLine(CheckReferencesReport checkReport)
    {
        var brokenCount = checkReport.BrokenReferences.Count;
        if (brokenCount == 0)
        {
            return "Every project: reference resolved.";
        }

        var referenceLabel = brokenCount == 1 ? "reference" : "references";

        return $"{brokenCount} project: {referenceLabel} did not resolve.";
    }
}
