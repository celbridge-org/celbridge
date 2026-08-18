using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Projects;
using Celbridge.Reports;
using Celbridge.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml.Controls;

namespace Celbridge.WorkspaceUI.ViewModels;

/// <summary>
/// One thing worth telling the user about, as the toast presents it: how serious it is, the single
/// line it reads, and the report holding the detail behind it.
/// </summary>
public record WorkspaceNotification(
    ReportSeverity Severity,
    string Message)
{
    /// <summary>
    /// The report a View Report action opens, or the empty key when the producer wrote none.
    /// </summary>
    public ResourceKey ReportResource { get; init; } = ResourceKey.Empty;
}

/// <summary>
/// Drives the workspace's single notification toast. One notification is on screen at a time and a
/// newer one replaces it, except that nothing less serious than an error displaces an error. Nothing
/// dismisses itself: the user may not have been at the machine when it appeared.
/// </summary>
public partial class WorkspaceToastViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly IDispatcher _dispatcher;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;

    private WorkspaceNotification? _current;

    [ObservableProperty]
    private bool _isToastVisible;

    [ObservableProperty]
    private InfoBarSeverity _toastSeverity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private string _toastMessage = string.Empty;

    [ObservableProperty]
    private bool _isActionVisible;

    public string ViewReportText => _stringLocalizer.GetString("Toast_ViewReportButton");

    public WorkspaceToastViewModel(
        IMessengerService messengerService,
        IDispatcher dispatcher,
        IStringLocalizer stringLocalizer,
        ICommandService commandService)
    {
        _messengerService = messengerService;
        _dispatcher = dispatcher;
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;

        _messengerService.Register<ProjectLoadNotificationMessage>(this, OnProjectLoadNotification);
        _messengerService.Register<ResourceOperationFailedMessage>(this, OnResourceOperationFailed);
    }

    private void OnProjectLoadNotification(object recipient, ProjectLoadNotificationMessage message)
    {
        // Raised from the load, which runs off the UI thread.
        _dispatcher.TryEnqueue(() => Show(ComposeLoadNotification(message.Summary)));
    }

    private void OnResourceOperationFailed(object recipient, ResourceOperationFailedMessage message)
    {
        _dispatcher.TryEnqueue(() => Show(ComposeOperationNotification(message)));
    }

    private WorkspaceNotification ComposeLoadNotification(ProjectLoadReportSummary summary)
    {
        var messageKey = summary.IssueCount == 1
            ? "Toast_ProjectLoadIssues_One"
            : "Toast_ProjectLoadIssues_Many";

        // The report's severity is the notification's. The project loaded either way — a version the
        // application cannot open never reaches the workspace at all.
        return Compose(summary.Severity, messageKey, summary.Resource, summary.IssueCount);
    }

    private WorkspaceNotification ComposeOperationNotification(ResourceOperationFailedMessage message)
    {
        var failedResources = message.FailedResources;
        if (failedResources.Count == 0)
        {
            // The operation ran; what it could not finish was rewriting the references into the
            // resources it moved, which leaves them pointing at the old location.
            var skippedCount = message.SkippedReferencers.Count;
            var skippedKey = skippedCount == 1
                ? "Toast_ReferencesNotUpdated_One"
                : "Toast_ReferencesNotUpdated_Many";

            return Compose(ReportSeverity.Warning, skippedKey, message.ReportResource, skippedCount);
        }

        var baseKey = message.OperationType switch
        {
            ResourceOperationType.Delete => "Toast_OperationFailed_Delete",
            ResourceOperationType.Copy => "Toast_OperationFailed_Copy",
            ResourceOperationType.Move => "Toast_OperationFailed_Move",
            ResourceOperationType.Rename => "Toast_OperationFailed_Rename",
            ResourceOperationType.Create => "Toast_OperationFailed_Create",
            ResourceOperationType.Archive => "Toast_OperationFailed_Archive",
            ResourceOperationType.Extract => "Toast_OperationFailed_Extract",
            _ => "Toast_OperationFailed_Unknown"
        };

        // One failure carries its reason, which is all a report would have said. Several are a count,
        // since neither the names nor the reasons fit one line and the report holds both.
        if (failedResources.Count == 1)
        {
            var failedResource = failedResources[0];
            var reason = SummarizeReason(failedResource.Message);

            return Compose(
                ReportSeverity.Error,
                $"{baseKey}_Single",
                message.ReportResource,
                failedResource.Resource.ResourceName,
                reason);
        }

        return Compose(
            ReportSeverity.Error,
            $"{baseKey}_Multiple",
            message.ReportResource,
            failedResources.Count);
    }

    // A failure reason is an outer-first chain of messages over several lines. The first line is the
    // summary, and the rest is what the report is for.
    private static string SummarizeReason(string reason)
    {
        var lineBreakIndex = reason.IndexOf('\n');
        if (lineBreakIndex < 0)
        {
            return reason.Trim();
        }

        return reason.Substring(0, lineBreakIndex).Trim();
    }

    private WorkspaceNotification Compose(
        ReportSeverity severity,
        string messageKey,
        ResourceKey reportResource,
        params object[] arguments)
    {
        var text = _stringLocalizer.GetString(messageKey, arguments);

        return new WorkspaceNotification(severity, text)
        {
            ReportResource = reportResource
        };
    }

    private void Show(WorkspaceNotification notification)
    {
        // An error stays until the user has seen it: a warning or an operation completing behind it does
        // not get to take its place. Another error does.
        if (_current is not null
            && _current.Severity == ReportSeverity.Error
            && notification.Severity != ReportSeverity.Error)
        {
            return;
        }

        _current = notification;

        ToastSeverity = ResolveToastSeverity(notification.Severity);
        ToastMessage = notification.Message;
        IsActionVisible = !notification.ReportResource.IsEmpty;
        IsToastVisible = true;
    }

    /// <summary>
    /// Dismisses the current notification once the user has closed it.
    /// </summary>
    public void OnToastDismissed()
    {
        _current = null;
        IsToastVisible = false;
    }

    /// <summary>
    /// Opens the report behind the current notification and dismisses it, since the report now carries
    /// everything the line was summarising.
    /// </summary>
    public void OnViewReportClicked()
    {
        var reportResource = _current?.ReportResource ?? ResourceKey.Empty;
        if (reportResource.IsEmpty)
        {
            return;
        }

        _commandService.Execute<IOpenDocumentCommand>(command => command.FileResource = reportResource);

        OnToastDismissed();
    }

    private static InfoBarSeverity ResolveToastSeverity(ReportSeverity severity)
    {
        switch (severity)
        {
            case ReportSeverity.Error:
                return InfoBarSeverity.Error;

            case ReportSeverity.Warning:
                return InfoBarSeverity.Warning;

            default:
                return InfoBarSeverity.Informational;
        }
    }

    public void Cleanup()
    {
        _messengerService.UnregisterAll(this);
    }
}
