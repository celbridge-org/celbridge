using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Reports;
using Celbridge.Utilities;

namespace Celbridge.Resources.Helpers;

// The report an operation writes: the id retention groups its history by, and the title the reader
// sees. Operations that can only ever fail on one resource have no kind, because a report holding a
// single row says nothing the notification did not.
internal partial record ResourceOperationReportKind(string Id, string Title);

/// <summary>
/// Tells the user what a resource operation could not do. One failure is fully expressed by the
/// notification line; several are written as a report the notification points at.
/// </summary>
public sealed class ResourceOperationNotifier
{
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

        var report = BuildReport(reportKind, operationType, failedResources, skippedReferencers);

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
        ResourceOperationType operationType,
        IReadOnlyList<FailedResource> failedResources,
        IReadOnlyList<SkippedReferencer> skippedReferencers)
    {
        var sections = new List<ReportSection>();

        if (failedResources.Count > 0)
        {
            sections.Add(BuildFailedResourcesSection(operationType, failedResources));
        }

        if (skippedReferencers.Count > 0)
        {
            sections.Add(BuildStaleReferencesSection(skippedReferencers));
        }

        var severity = failedResources.Count > 0
            ? ReportSeverity.Error
            : ReportSeverity.Warning;

        var summary = ComposeSummaryLine(operationType, failedResources.Count, skippedReferencers.Count);

        return new ReportDocument(
            reportKind.Id,
            reportKind.Title,
            DateTimeOffset.UtcNow,
            severity,
            summary,
            sections);
    }

    private static ReportSection BuildFailedResourcesSection(
        ResourceOperationType operationType,
        IReadOnlyList<FailedResource> failedResources)
    {
        var descriptor = ResolveFailureDescriptor(operationType);

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

    private static string ComposeSummaryLine(
        ResourceOperationType operationType,
        int failedCount,
        int skippedCount)
    {
        var staleClause = ComposeStaleClause(skippedCount);

        if (failedCount == 0)
        {
            return $"The operation completed, but {staleClause}.";
        }

        var resourceLabel = failedCount == 1 ? "resource" : "resources";
        var verb = ResolveFailureVerb(operationType);
        var failedClause = $"{failedCount} {resourceLabel} could not be {verb}";

        if (skippedCount == 0)
        {
            return $"{failedClause}.";
        }

        return $"{failedClause}, and {staleClause}.";
    }

    private static string ComposeStaleClause(int skippedCount)
    {
        var referenceLabel = skippedCount == 1 ? "reference was" : "references were";

        return $"{skippedCount} {referenceLabel} left pointing at the old location";
    }

    private static ResourceOperationReportKind? ResolveReportKind(ResourceOperationType operationType)
    {
        switch (operationType)
        {
            case ResourceOperationType.Copy:
                return new ResourceOperationReportKind("copy-resources", "Copy Resources");

            case ResourceOperationType.Move:
                return new ResourceOperationReportKind("move-resources", "Move Resources");

            case ResourceOperationType.Delete:
                return new ResourceOperationReportKind("delete-resources", "Delete Resources");

            default:
                return null;
        }
    }

    private static ReportFindingDescriptor ResolveFailureDescriptor(ResourceOperationType operationType)
    {
        switch (operationType)
        {
            case ResourceOperationType.Copy:
                return ReportFindingCatalog.Resource.CopyFailed;

            case ResourceOperationType.Delete:
                return ReportFindingCatalog.Resource.DeleteFailed;

            default:
                return ReportFindingCatalog.Resource.MoveFailed;
        }
    }

    private static string ResolveFailureVerb(ResourceOperationType operationType)
    {
        switch (operationType)
        {
            case ResourceOperationType.Copy:
                return "copied";

            case ResourceOperationType.Delete:
                return "deleted";

            default:
                return "moved";
        }
    }
}
