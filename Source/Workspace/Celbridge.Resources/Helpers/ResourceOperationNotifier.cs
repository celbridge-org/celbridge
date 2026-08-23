using Celbridge.Localization;
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
    string TitleKey,
    ReportFindingDescriptor FailureDescriptor,
    ResourceOperationSummaryKeys FailureSummary);

// Keys naming whole sentences that take the count, rather than a participle the summary line splices
// into a sentence it builds itself. A bare verb carries no number agreement and no word order, so it
// is not something a translation could be written against.
internal record ResourceOperationSummaryKeys(
    string SingleFailure,
    string MultipleFailures);

/// <summary>
/// Tells the user what a resource operation could not do. One failure is fully expressed by the
/// notification line; several are written as a report the notification points at.
/// </summary>
public sealed class ResourceOperationNotifier
{
    // Stale references read the same whichever operation left them, so these are not per-operation.
    private const string OperationCompletedKey = "Report_ResourceOperation_Summary_Completed";
    private const string SingleStaleReferenceKey = "Report_ResourceOperation_Summary_StaleReferences_One";
    private const string MultipleStaleReferencesKey = "Report_ResourceOperation_Summary_StaleReferences_Many";
    private const string ResourcesSectionKey = "Report_ResourceOperation_Section_Resources";
    private const string ReferencesSectionKey = "Report_ResourceOperation_Section_References";
    private const string OpenResourceActionKey = "Report_Action_OpenResource";

    private readonly ILogger<ResourceOperationNotifier> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IProjectService _projectService;
    private readonly IReportWriter _reportWriter;
    private readonly ILocalizerService _localizerService;

    public ResourceOperationNotifier(
        ILogger<ResourceOperationNotifier> logger,
        IMessengerService messengerService,
        IProjectService projectService,
        IReportWriter reportWriter,
        ILocalizerService localizerService)
    {
        _logger = logger;
        _messengerService = messengerService;
        _projectService = projectService;
        _reportWriter = reportWriter;
        _localizerService = localizerService;
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

        // Both surfaces below are read by a person, so a typed reason becomes a localized sentence here.
        // The caller's own list keeps the operation's message, which is what a batch command returns to a
        // programmatic caller.
        failedResources = ResourceOperationFailureFormatter.Localize(failedResources, _localizerService);

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

        var writeResult = await ReportLocation.WriteReportAsync(_reportWriter, report, currentProject.ProjectDataFolderPath);
        if (writeResult.IsFailure)
        {
            _logger.LogWarning(writeResult, "Failed to write the resource operation report.");
            return ResourceKey.Empty;
        }

        return writeResult.Value;
    }

    private ReportDocument BuildReport(
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
            _localizerService.GetString(reportKind.TitleKey),
            DateTimeOffset.UtcNow,
            severity,
            summary,
            sections);
    }

    private ReportSection BuildFailedResourcesSection(
        ResourceOperationReportKind reportKind,
        IReadOnlyList<FailedResource> failedResources)
    {
        var descriptor = reportKind.FailureDescriptor;

        var items = new List<ReportItem>(failedResources.Count);
        foreach (var failedResource in failedResources)
        {
            var item = ReportFinding.Create(_localizerService, descriptor) with
            {
                Resource = failedResource.Resource,
                Detail = failedResource.Message,
                Actions = ComposeOpenActions(failedResource.Resource)
            };

            items.Add(item);
        }

        var title = _localizerService.GetString(ResourcesSectionKey);

        return new ReportSection(title, ReportSectionKind.Findings, ReportSeverity.Error, items);
    }

    private ReportSection BuildStaleReferencesSection(IReadOnlyList<SkippedReferencer> skippedReferencers)
    {
        var items = new List<ReportItem>(skippedReferencers.Count);
        foreach (var skippedReferencer in skippedReferencers)
        {
            var item = ReportFinding.Create(_localizerService, ReportFindingCatalog.Resource.ReferenceNotUpdated) with
            {
                Resource = skippedReferencer.Resource,
                Detail = skippedReferencer.Message,
                Actions = ComposeOpenActions(skippedReferencer.Resource)
            };

            items.Add(item);
        }

        var title = _localizerService.GetString(ReferencesSectionKey);

        return new ReportSection(title, ReportSectionKind.Findings, ReportSeverity.Warning, items);
    }

    // No location: the failure is about the resource as a whole, not a position inside it. A failure
    // usually leaves its resource in place, so the row opens it; where it does not, the open reports
    // the resource as missing and the row still says what failed and why.
    private IReadOnlyList<ReportAction> ComposeOpenActions(ResourceKey resource)
    {
        var label = _localizerService.GetString(OpenResourceActionKey, resource.ResourceName);

        var action = new ReportAction(ReportActionKind.OpenResource, label)
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
    private string ComposeSummaryLine(
        ResourceOperationReportKind reportKind,
        int failedCount,
        int skippedCount)
    {
        var sentences = new List<string>();

        if (failedCount > 0)
        {
            var failureKey = failedCount == 1
                ? reportKind.FailureSummary.SingleFailure
                : reportKind.FailureSummary.MultipleFailures;

            sentences.Add(_localizerService.GetString(failureKey, failedCount));
        }
        else
        {
            sentences.Add(_localizerService.GetString(OperationCompletedKey));
        }

        if (skippedCount > 0)
        {
            var staleKey = skippedCount == 1
                ? SingleStaleReferenceKey
                : MultipleStaleReferencesKey;

            sentences.Add(_localizerService.GetString(staleKey, skippedCount));
        }

        return string.Join(" ", sentences);
    }

    // Null for every other operation, because they act on one resource at a time and so can never
    // reach the report path at all.
    private static ResourceOperationReportKind? ResolveReportKind(ResourceOperationType operationType)
    {
        switch (operationType)
        {
            case ResourceOperationType.Copy:
            {
                var summary = new ResourceOperationSummaryKeys(
                    "Report_ResourceOperation_Summary_Copy_One",
                    "Report_ResourceOperation_Summary_Copy_Many");

                return new ResourceOperationReportKind(
                    "copy-resources",
                    "Report_ResourceOperation_Title_Copy",
                    ReportFindingCatalog.Resource.CopyFailed,
                    summary);
            }

            case ResourceOperationType.Move:
            {
                var summary = new ResourceOperationSummaryKeys(
                    "Report_ResourceOperation_Summary_Move_One",
                    "Report_ResourceOperation_Summary_Move_Many");

                return new ResourceOperationReportKind(
                    "move-resources",
                    "Report_ResourceOperation_Title_Move",
                    ReportFindingCatalog.Resource.MoveFailed,
                    summary);
            }

            case ResourceOperationType.Delete:
            {
                var summary = new ResourceOperationSummaryKeys(
                    "Report_ResourceOperation_Summary_Delete_One",
                    "Report_ResourceOperation_Summary_Delete_Many");

                return new ResourceOperationReportKind(
                    "delete-resources",
                    "Report_ResourceOperation_Title_Delete",
                    ReportFindingCatalog.Resource.DeleteFailed,
                    summary);
            }

            default:
                return null;
        }
    }
}
