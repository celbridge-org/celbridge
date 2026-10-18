using Celbridge.Localization;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Reports;
using Celbridge.Utilities;

namespace Celbridge.Resources.Helpers;

// How an operation that failed on more than one resource reports itself: the id its history is kept
// under, the title the reader sees, the finding every failed resource is an occurrence of, and the
// summary line. Only the operations that can fail on more than one resource have one. A report with a
// single row would repeat the notification. The summary key names a whole sentence that takes the
// count. A translation then sets its own word order and number agreement.
internal partial record ResourceOperationReportKind(
    string Id,
    string TitleKey,
    ReportFindingDescriptor FailureDescriptor,
    string FailureSummaryKey);

/// <summary>
/// Tells the user what a resource operation could not do. One failure is fully expressed by the
/// notification line; several are written as a report the notification points at.
/// </summary>
public sealed class ResourceOperationNotifier
{
    private const string ResourcesSectionKey = "Report_ResourceOperation_Section_Resources";
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
        if (failedResources.Count == 0)
        {
            return;
        }

        // Both surfaces below are read by a person, so a typed reason becomes a localized sentence here.
        // The caller's own list keeps the operation's message, which is what a batch command returns to a
        // programmatic caller.
        failedResources = ResourceOperationFailureFormatter.Localize(failedResources, _localizerService);

        var reportResource = ResourceKey.Empty;
        if (failedResources.Count > 1)
        {
            reportResource = await WriteReportAsync(operationType, failedResources);
        }

        var message = new ResourceOperationFailedMessage(operationType, failedResources)
        {
            ReportResource = reportResource
        };

        _messengerService.Send(message);
    }

    // The operation itself has already run, so a report that could not be written is logged rather
    // than turned into a second failure. The notification still fires, without its action.
    private async Task<ResourceKey> WriteReportAsync(
        ResourceOperationType operationType,
        IReadOnlyList<FailedResource> failedResources)
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

        var report = BuildReport(reportKind, failedResources);

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
        IReadOnlyList<FailedResource> failedResources)
    {
        var sections = new List<ReportSection>
        {
            BuildFailedResourcesSection(reportKind, failedResources)
        };

        var summary = _localizerService.GetString(reportKind.FailureSummaryKey, failedResources.Count);

        return new ReportDocument(
            reportKind.Id,
            _localizerService.GetString(reportKind.TitleKey),
            DateTimeOffset.UtcNow,
            ReportSeverity.Error,
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

    // Null for every other operation, because they act on one resource at a time and so can never
    // reach the report path at all.
    private static ResourceOperationReportKind? ResolveReportKind(ResourceOperationType operationType)
    {
        switch (operationType)
        {
            case ResourceOperationType.Copy:
                return new ResourceOperationReportKind(
                    "copy-resources",
                    "Report_ResourceOperation_Title_Copy",
                    ReportFindingCatalog.Resource.CopyFailed,
                    "Report_ResourceOperation_Summary_Copy_Many");

            case ResourceOperationType.Move:
                return new ResourceOperationReportKind(
                    "move-resources",
                    "Report_ResourceOperation_Title_Move",
                    ReportFindingCatalog.Resource.MoveFailed,
                    "Report_ResourceOperation_Summary_Move_Many");

            case ResourceOperationType.Delete:
                return new ResourceOperationReportKind(
                    "delete-resources",
                    "Report_ResourceOperation_Title_Delete",
                    ReportFindingCatalog.Resource.DeleteFailed,
                    "Report_ResourceOperation_Summary_Delete_Many");

            default:
                return null;
        }
    }
}
