using Celbridge.Documents;
using Celbridge.Localization;
using Celbridge.Notifications;
using Celbridge.Projects;
using Celbridge.Reports;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Records what the application's notification sources report as entries in the notification centre. A
/// project load that found issues is a condition. A failed resource operation, a discarded save and a
/// notification raised by an editor are each an event.
/// </summary>
public sealed class NotificationComposer
{
    private const string ProjectLoadSource = "project-load";

    private readonly IMessengerService _messengerService;
    private readonly ILocalizerService _localizerService;
    private readonly INotificationCentre _notificationCentre;

    private bool _isStarted;

    public NotificationComposer(
        IMessengerService messengerService,
        ILocalizerService localizerService,
        INotificationCentre notificationCentre)
    {
        _messengerService = messengerService;
        _localizerService = localizerService;
        _notificationCentre = notificationCentre;
    }

    /// <summary>
    /// Starts recording what the sources report. Nothing reported before this is recorded.
    /// </summary>
    public void Start()
    {
        if (_isStarted)
        {
            return;
        }

        _isStarted = true;

        // Each source raises its message on whichever thread it runs on, and the centre accepts calls from any.
        _messengerService.Register<ProjectLoadNotificationMessage>(this, OnProjectLoadNotification);
        _messengerService.Register<ResourceOperationFailedMessage>(this, OnResourceOperationFailed);
        _messengerService.Register<WorkspaceItemSaveDiscardedMessage>(this, OnWorkspaceItemSaveDiscarded);
        _messengerService.Register<EditorNotificationMessage>(this, OnEditorNotification);
    }

    private void OnProjectLoadNotification(object recipient, ProjectLoadNotificationMessage message)
    {
        var content = ComposeLoadNotification(message.Summary);
        _notificationCentre.RecordCondition(ProjectLoadSource, content);
    }

    private void OnResourceOperationFailed(object recipient, ResourceOperationFailedMessage message)
    {
        var content = ComposeOperationNotification(message);
        _notificationCentre.AddEvent(content);
    }

    private void OnWorkspaceItemSaveDiscarded(object recipient, WorkspaceItemSaveDiscardedMessage message)
    {
        var content = Compose(
            ReportSeverity.Error,
            "Notification_SaveDiscarded",
            action: null,
            message.Resource.ResourceName);

        _notificationCentre.AddEvent(content);
    }

    // The editor resolved the text itself, so no key is looked up.
    private void OnEditorNotification(object recipient, EditorNotificationMessage message)
    {
        var text = ToSingleLine(message.Message);

        var content = new NotificationContent(message.Severity, text)
        {
            Action = ComposeEditorAction(message.Action)
        };

        _notificationCentre.AddEvent(content);
    }

    // An action the editor gave no label takes the host's wording for opening a report.
    private OpenDocumentAction? ComposeEditorAction(OpenDocumentAction? action)
    {
        if (action is null ||
            !string.IsNullOrEmpty(action.Label))
        {
            return action;
        }

        var label = _localizerService.GetString("Notification_ViewReportButton");

        return action with
        {
            Label = label
        };
    }

    private NotificationContent ComposeLoadNotification(ProjectLoadReportSummary summary)
    {
        var messageKey = summary.IssueCount == 1
            ? "Notification_ProjectLoadIssues_One"
            : "Notification_ProjectLoadIssues_Many";

        // The report's severity is the notification's. The project loaded either way, because a version the
        // application cannot open never reaches the workspace at all.
        return Compose(summary.Severity, messageKey, ComposeReportAction(summary.Resource), summary.IssueCount);
    }

    private NotificationContent ComposeOperationNotification(ResourceOperationFailedMessage message)
    {
        var failedResources = message.FailedResources;
        if (failedResources.Count == 0)
        {
            // The operation ran. What it could not finish was rewriting the references into the resources it
            // moved, which leaves them pointing at the old location.
            var skippedCount = message.SkippedReferencers.Count;
            var skippedKey = skippedCount == 1
                ? "Notification_ReferencesNotUpdated_One"
                : "Notification_ReferencesNotUpdated_Many";

            return Compose(
                ReportSeverity.Warning,
                skippedKey,
                ComposeReportAction(message.ReportResource),
                skippedCount);
        }

        var baseKey = message.OperationType switch
        {
            ResourceOperationType.Delete => "Notification_OperationFailed_Delete",
            ResourceOperationType.Copy => "Notification_OperationFailed_Copy",
            ResourceOperationType.Move => "Notification_OperationFailed_Move",
            ResourceOperationType.Rename => "Notification_OperationFailed_Rename",
            ResourceOperationType.Create => "Notification_OperationFailed_Create",
            ResourceOperationType.Archive => "Notification_OperationFailed_Archive",
            ResourceOperationType.Extract => "Notification_OperationFailed_Extract",
            _ => "Notification_OperationFailed_Unknown"
        };

        // One failure carries its reason, which is all a report would have said. Several are a count, since
        // neither the names nor the reasons fit one line and the report holds both.
        if (failedResources.Count == 1)
        {
            var failedResource = failedResources[0];
            var reason = ToSingleLine(failedResource.Message);

            return Compose(
                ReportSeverity.Error,
                $"{baseKey}_Single",
                ComposeReportAction(message.ReportResource),
                failedResource.Resource.ResourceName,
                reason);
        }

        return Compose(
            ReportSeverity.Error,
            $"{baseKey}_Multiple",
            ComposeReportAction(message.ReportResource),
            failedResources.Count);
    }

    // A notification is one line. A failure reason is an outer-first chain over several lines, and an editor
    // can pass anything, so both are cut to their first line and the rest is what a report is for.
    private static string ToSingleLine(string text)
    {
        var lineBreakIndex = text.IndexOf('\n');
        if (lineBreakIndex < 0)
        {
            return text.Trim();
        }

        return text.Substring(0, lineBreakIndex).Trim();
    }

    // A host source's action always opens the report it just wrote, so the host supplies the label.
    private OpenDocumentAction? ComposeReportAction(ResourceKey reportResource)
    {
        if (reportResource.IsEmpty)
        {
            return null;
        }

        var label = _localizerService.GetString("Notification_ViewReportButton");

        return new OpenDocumentAction(reportResource, label);
    }

    private NotificationContent Compose(
        ReportSeverity severity,
        string messageKey,
        OpenDocumentAction? action,
        params object[] arguments)
    {
        var text = _localizerService.GetString(messageKey, arguments);

        return new NotificationContent(severity, text)
        {
            Action = action
        };
    }
}
