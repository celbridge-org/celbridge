using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Projects;
using Celbridge.Reports;
using Celbridge.Resources;
using Celbridge.Utilities;
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
    /// The document the notification's action opens, or null when the producer offered none.
    /// </summary>
    public OpenDocumentAction? Action { get; init; }
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

    [ObservableProperty]
    private string _actionLabel = string.Empty;

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
        _messengerService.Register<EditorNotificationMessage>(this, OnEditorNotification);
        _messengerService.Register<WorkspaceItemSaveFailedMessage>(this, OnWorkspaceItemSaveFailed);
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

    private void OnWorkspaceItemSaveFailed(object recipient, WorkspaceItemSaveFailedMessage message)
    {
        // Raised from the workspace update loop, which does not run on the UI thread.
        _dispatcher.TryEnqueue(() => Show(ComposeSaveFailureNotification(message)));
    }

    private void OnEditorNotification(object recipient, EditorNotificationMessage message)
    {
        // Raised from a WebView's RPC handler, which does not run on the UI thread.
        _dispatcher.TryEnqueue(() => Show(ComposeEditorNotification(message)));
    }

    // The editor resolved the text, so it is shown as written rather than composed from a key.
    private static WorkspaceNotification ComposeEditorNotification(EditorNotificationMessage message)
    {
        var text = ToSingleLine(message.Message);

        return new WorkspaceNotification(message.Severity, text)
        {
            Action = message.Action
        };
    }

    private WorkspaceNotification ComposeSaveFailureNotification(WorkspaceItemSaveFailedMessage message)
    {
        var failedItems = message.FailedItems;

        if (failedItems.Count == 1)
        {
            var failedItem = failedItems[0];
            var reason = ToSingleLine(failedItem.Message);

            return Compose(
                ReportSeverity.Error,
                "Toast_SaveFailed_Single",
                action: null,
                failedItem.Resource.ResourceName,
                reason);
        }

        return Compose(
            ReportSeverity.Error,
            "Toast_SaveFailed_Multiple",
            action: null,
            failedItems.Count);
    }

    private WorkspaceNotification ComposeLoadNotification(ProjectLoadReportSummary summary)
    {
        var messageKey = summary.IssueCount == 1
            ? "Toast_ProjectLoadIssues_One"
            : "Toast_ProjectLoadIssues_Many";

        // The report's severity is the notification's. The project loaded either way — a version the
        // application cannot open never reaches the workspace at all.
        return Compose(summary.Severity, messageKey, ComposeReportAction(summary.Resource), summary.IssueCount);
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

            return Compose(
                ReportSeverity.Warning,
                skippedKey,
                ComposeReportAction(message.ReportResource),
                skippedCount);
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

    // A toast is one line. A failure reason is an outer-first chain over several lines, and an editor
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

    // A host producer's action always opens the report it just wrote, and always says so, so the label
    // is the host's own rather than one the producer has to supply.
    private OpenDocumentAction? ComposeReportAction(ResourceKey reportResource)
    {
        if (reportResource.IsEmpty)
        {
            return null;
        }

        var label = _stringLocalizer.GetString("Toast_ViewReportButton");

        return new OpenDocumentAction(reportResource, label);
    }

    private WorkspaceNotification Compose(
        ReportSeverity severity,
        string messageKey,
        OpenDocumentAction? action,
        params object[] arguments)
    {
        var text = _stringLocalizer.GetString(messageKey, arguments);

        return new WorkspaceNotification(severity, text)
        {
            Action = action
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

        var action = notification.Action;

        ToastSeverity = ResolveToastSeverity(notification.Severity);
        ToastMessage = notification.Message;
        ActionLabel = action?.Label ?? _stringLocalizer.GetString("Toast_ViewReportButton");
        IsActionVisible = action is not null;
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
    /// Opens the document behind the current notification and dismisses it, since what the line was
    /// summarising is now on screen.
    /// </summary>
    public void OnActionClicked()
    {
        var action = _current?.Action;
        if (action is null)
        {
            return;
        }

        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = action.Resource;
            command.Location = DocumentLocation.Compose(action.Line, action.Column);
        });

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
