using System.Globalization;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Reports;
using Celbridge.Utilities;

namespace Celbridge.Resources.Helpers;

// How an operation that failed on more than one resource reports itself: the id its history is kept
// under, the title the reader sees, the finding every failed resource is an occurrence of, and the
// sentences its summary line is written from. Only the operations that can fail on more than one
// resource have one, because a report holding a single row says nothing the notification did not.
internal partial record ResourceOperationReportKind(
    string Id,
    string Title,
    ReportFindingDescriptor FailureDescriptor,
    ResourceOperationSummary FailureSummary);

// Whole sentences taking the count as {0}, rather than a participle the summary line splices into a
// sentence it builds itself. A bare verb carries no number agreement and no word order, so it is not
// something a translation of this wording could be written against.
internal record ResourceOperationSummary(
    string SingleFailure,
    string MultipleFailures);

/// <summary>
/// Tells the user what a resource operation could not do. One failure is fully expressed by the
/// notification line; several are written as a report the notification points at.
/// </summary>
public sealed class ResourceOperationNotifier
{
    // Stale references read the same whichever operation left them, so these are not per-operation.
    private const string OperationCompleted = "The operation completed.";
    private const string SingleStaleReference = "{0} reference was left pointing at the old location.";
    private const string MultipleStaleReferences = "{0} references were left pointing at the old location.";

    private readonly ILogger<ResourceOperationNotifier> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IProjectService _projectService;
    private readonly IReportWriter _reportWriter;

    public ResourceOperationNotifier(
        ILogger<ResourceOperationNotifier> logger,
        IMessengerService messengerService,
        IProjectService projectService,
        IReportWriter reportWriter)
    {
        _logger = logger;
        _messengerService = messengerService;
        _projectService = projectService;
        _reportWriter = reportWriter;
    }

    /// <summary>
    /// Reports the resources an operation failed on, writing a report when there is more than one.
    /// </summary>
    public async Task NotifyFailuresAsync(
        ResourceOperationType operationType,
        IReadOnlyList<FailedResource> failedResources)
    {
        await NotifyFailuresAsync(operationType, failedResources, Array.Empty<SkippedReferencer>());
    }

    /// <summary>
    /// Reports what an operation could not finish: the resources it failed on, and the references
    /// into the resources it moved that it could not rewrite.
    /// </summary>
    public async Task NotifyFailuresAsync(
        ResourceOperationType operationType,
        IReadOnlyList<FailedResource> failedResources,
        IReadOnlyList<SkippedReferencer> skippedReferencers)
    {
        var itemCount = failedResources.Count + skippedReferencers.Count;
        if (itemCount == 0)
        {
            return;
        }

        var reportResource = ResourceKey.Empty;
        if (itemCount > 1)
        {
            reportResource = await WriteReportAsync(operationType, failedResources, skippedReferencers);
        }

        var message = new ResourceOperationFailedMessage(operationType, failedResources)
        {
            SkippedReferencers = skippedReferencers,
            ReportResource = reportResource
        };

        _messengerService.Send(message);
    }

    // The operation itself has already run, so a report that could not be written is logged rather
    // than turned into a second failure. The notification still fires, without its action.
    private async Task<ResourceKey> WriteReportAsync(
        ResourceOperationType operationType,
        IReadOnlyList<FailedResource> failedResources,
        IReadOnlyList<SkippedReferencer> skippedReferencers)
    {
        var reportKind = ResolveReportKind(operationType);
        if (reportKind is null)
        {
            return ResourceKey.Empty;
        }

        var currentProject = _projectService.CurrentProject;
        if (currentProject is null)
        {
            return ResourceKey.Empty;
        }

        var report = BuildReport(reportKind, failedResources, skippedReferencers);

        var writeResult = await ReportLocation.WriteReportAsync(_reportWriter, report, currentProject.ProjectFilePath);
        if (writeResult.IsFailure)
        {
            _logger.LogWarning(writeResult, "Failed to write the resource operation report.");
            return ResourceKey.Empty;
        }

        return writeResult.Value;
    }

    private static ReportDocument BuildReport(
        ResourceOperationReportKind reportKind,
        IReadOnlyList<FailedResource> failedResources,
        IReadOnlyList<SkippedReferencer> skippedReferencers)
    {
        var sections = new List<ReportSection>();

        if (failedResources.Count > 0)
        {
            sections.Add(BuildFailedResourcesSection(reportKind, failedResources));
        }

        if (skippedReferencers.Count > 0)
        {
            sections.Add(BuildStaleReferencesSection(skippedReferencers));
        }

        var severity = failedResources.Count > 0
            ? ReportSeverity.Error
            : ReportSeverity.Warning;

        var summary = ComposeSummaryLine(reportKind, failedResources.Count, skippedReferencers.Count);

        return new ReportDocument(
            reportKind.Id,
            reportKind.Title,
            DateTimeOffset.UtcNow,
            severity,
            summary,
            sections);
    }

    private static ReportSection BuildFailedResourcesSection(
        ResourceOperationReportKind reportKind,
        IReadOnlyList<FailedResource> failedResources)
    {
        var descriptor = reportKind.FailureDescriptor;

        var items = new List<ReportItem>(failedResources.Count);
        foreach (var failedResource in failedResources)
        {
            var item = ReportFinding.Create(descriptor) with
            {
                Resource = failedResource.Resource,
                Detail = failedResource.Message,
                Actions = ComposeOpenActions(failedResource.Resource)
            };

            items.Add(item);
        }

        return new ReportSection("Resources", ReportSectionKind.Findings, ReportSeverity.Error, items);
    }

    private static ReportSection BuildStaleReferencesSection(IReadOnlyList<SkippedReferencer> skippedReferencers)
    {
        var items = new List<ReportItem>(skippedReferencers.Count);
        foreach (var skippedReferencer in skippedReferencers)
        {
            var item = ReportFinding.Create(ReportFindingCatalog.Resource.ReferenceNotUpdated) with
            {
                Resource = skippedReferencer.Resource,
                Detail = skippedReferencer.Message,
                Actions = ComposeOpenActions(skippedReferencer.Resource)
            };

            items.Add(item);
        }

        return new ReportSection("References", ReportSectionKind.Findings, ReportSeverity.Warning, items);
    }

    // No location: the failure is about the resource as a whole, not a position inside it. A failure
    // usually leaves its resource in place, so the row opens it; where it does not, the open reports
    // the resource as missing and the row still says what failed and why.
    private static IReadOnlyList<ReportAction> ComposeOpenActions(ResourceKey resource)
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

    // One sentence per fact, joined by a space. Conjoining them into a single sentence would fix the
    // conjunction and the clause order of one language in code rather than in the wording itself.
    private static string ComposeSummaryLine(
        ResourceOperationReportKind reportKind,
        int failedCount,
        int skippedCount)
    {
        var sentences = new List<string>();

        if (failedCount > 0)
        {
            var failureTemplate = failedCount == 1
                ? reportKind.FailureSummary.SingleFailure
                : reportKind.FailureSummary.MultipleFailures;

            sentences.Add(ComposeSentence(failureTemplate, failedCount));
        }
        else
        {
            sentences.Add(OperationCompleted);
        }

        if (skippedCount > 0)
        {
            var staleTemplate = skippedCount == 1
                ? SingleStaleReference
                : MultipleStaleReferences;

            sentences.Add(ComposeSentence(staleTemplate, skippedCount));
        }

        return string.Join(" ", sentences);
    }

    // Invariant culture, matching the finding messages composed alongside these in the same report.
    private static string ComposeSentence(string template, int count)
    {
        return string.Format(CultureInfo.InvariantCulture, template, count);
    }

    // Null for every other operation, because they act on one resource at a time and so can never
    // reach the report path at all.
    private static ResourceOperationReportKind? ResolveReportKind(ResourceOperationType operationType)
    {
        switch (operationType)
        {
            case ResourceOperationType.Copy:
            {
                var summary = new ResourceOperationSummary(
                    "{0} resource could not be copied.",
                    "{0} resources could not be copied.");

                return new ResourceOperationReportKind(
                    "copy-resources",
                    "Copy Resources",
                    ReportFindingCatalog.Resource.CopyFailed,
                    summary);
            }

            case ResourceOperationType.Move:
            {
                var summary = new ResourceOperationSummary(
                    "{0} resource could not be moved.",
                    "{0} resources could not be moved.");

                return new ResourceOperationReportKind(
                    "move-resources",
                    "Move Resources",
                    ReportFindingCatalog.Resource.MoveFailed,
                    summary);
            }

            case ResourceOperationType.Delete:
            {
                var summary = new ResourceOperationSummary(
                    "{0} resource could not be deleted.",
                    "{0} resources could not be deleted.");

                return new ResourceOperationReportKind(
                    "delete-resources",
                    "Delete Resources",
                    ReportFindingCatalog.Resource.DeleteFailed,
                    summary);
            }

            default:
                return null;
        }
    }
}
