using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Projects;
using Celbridge.Reports;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml.Controls;

namespace Celbridge.WorkspaceUI.ViewModels;

/// <summary>
/// What the user can do about a notification, offered as the banner's action button.
/// </summary>
public enum NotificationAction
{
    None,
    ReloadProject,
    ViewReport
}

/// <summary>
/// One condition worth telling the user about: the text of its banner, how serious it is, and what
/// the user can do about it.
/// </summary>
public record WorkspaceNotification(
    ReportSeverity Severity,
    string Title,
    string Message)
{
    public NotificationAction Action { get; init; } = NotificationAction.None;

    /// <summary>
    /// The report a ViewReport action opens.
    /// </summary>
    public ResourceKey ReportResource { get; init; } = ResourceKey.Empty;

    /// <summary>
    /// Whether the user can dismiss the banner and move on to the next notification.
    /// </summary>
    public bool IsDismissable { get; init; } = true;
}

/// <summary>
/// Tracks project-scoped conditions worth telling the user about, such as a config file that failed
/// to load or was only partly applied, and projects the current one onto the notification bar's banner.
/// </summary>
public partial class NotificationBarViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly IDispatcher _dispatcher;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IProjectService _projectService;
    private readonly ICommandService _commandService;

    private readonly List<WorkspaceNotification> _notifications = new();

    private int _currentIndex;

    [ObservableProperty]
    private bool _isBannerVisible;

    [ObservableProperty]
    private InfoBarSeverity _bannerSeverity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private string _bannerTitle = string.Empty;

    [ObservableProperty]
    private string _bannerMessage = string.Empty;

    [ObservableProperty]
    private bool _isBannerDismissable = true;

    [ObservableProperty]
    private bool _isActionVisible;

    [ObservableProperty]
    private string _actionText = string.Empty;

    public NotificationBarViewModel(
        IMessengerService messengerService,
        IDispatcher dispatcher,
        IStringLocalizer stringLocalizer,
        IProjectService projectService,
        ICommandService commandService)
    {
        _messengerService = messengerService;
        _dispatcher = dispatcher;
        _stringLocalizer = stringLocalizer;
        _projectService = projectService;
        _commandService = commandService;

        // Register for project error messages
        _messengerService.Register<ProjectErrorMessage>(this, OnProjectError);

        // Check if the project was migrated and show banner if needed
        CheckMigrationStatus();
    }

    private void OnProjectError(object recipient, ProjectErrorMessage message)
    {
        // This handler may be called from a background thread so ensure that the message
        // is handled on the main UI thread.
        _dispatcher.TryEnqueue(() =>
        {
            AddNotification(ComposeNotification(message));
        });
    }

    private WorkspaceNotification ComposeNotification(ProjectErrorMessage message)
    {
        var configFile = message.ConfigFileName ?? "project configuration file";

        switch (message.ErrorType)
        {
            case ProjectErrorType.InvalidProjectConfig:
                return ComposeProjectLoadFailure(
                    _stringLocalizer.GetString("NotificationBar_ProjectConfigErrorTitle"),
                    _stringLocalizer.GetString("NotificationBar_ProjectConfigErrorMessage", configFile));

            case ProjectErrorType.IncompatibleVersion:
                return ComposeProjectLoadFailure(
                    _stringLocalizer.GetString("NotificationBar_IncompatibleVersionTitle"),
                    _stringLocalizer.GetString("NotificationBar_IncompatibleVersionMessage", configFile));

            case ProjectErrorType.InvalidVersion:
                return ComposeProjectLoadFailure(
                    _stringLocalizer.GetString("NotificationBar_InvalidVersionTitle"),
                    _stringLocalizer.GetString("NotificationBar_InvalidVersionMessage", configFile));

            case ProjectErrorType.MigrationError:
                return ComposeProjectLoadFailure(
                    _stringLocalizer.GetString("NotificationBar_MigrationErrorTitle"),
                    _stringLocalizer.GetString("NotificationBar_MigrationErrorMessage", configFile));

            case ProjectErrorType.PackageLoadError:
                return ComposeProjectLoadFailure(
                    _stringLocalizer.GetString("NotificationBar_PackageLoadErrorTitle"),
                    _stringLocalizer.GetString("NotificationBar_PackageLoadErrorMessage"));

            case ProjectErrorType.ProjectCheckError:
                // Project check findings are advisory, not blocking: the project loaded fine.
                return ComposeAdvisory(
                    _stringLocalizer.GetString("NotificationBar_ProjectCheckFindingsTitle"),
                    _stringLocalizer.GetString("NotificationBar_ProjectCheckFindingsMessage", message.FindingCount),
                    message.ReportResource);

            case ProjectErrorType.ProjectConfigEntryError:
                // Per-entry config errors are advisory: the rest of the file applied and the project loaded.
                return ComposeAdvisory(
                    _stringLocalizer.GetString("NotificationBar_ProjectConfigEntryErrorTitle"),
                    _stringLocalizer.GetString("NotificationBar_ProjectConfigEntryErrorMessage", configFile),
                    message.ReportResource);

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    // A project that did not load stays on screen until the user reloads it, so its banner offers the
    // reload rather than a dismissal.
    private static WorkspaceNotification ComposeProjectLoadFailure(string title, string message)
    {
        return new WorkspaceNotification(ReportSeverity.Error, title, message)
        {
            Action = NotificationAction.ReloadProject,
            IsDismissable = false
        };
    }

    private static WorkspaceNotification ComposeAdvisory(string title, string message, ResourceKey? reportResource)
    {
        var notification = new WorkspaceNotification(ReportSeverity.Warning, title, message);

        if (reportResource is not ResourceKey resource)
        {
            return notification;
        }

        return notification with
        {
            Action = NotificationAction.ViewReport,
            ReportResource = resource
        };
    }

    private void AddNotification(WorkspaceNotification notification)
    {
        int insertIndex = _notifications.Count;

        if (notification.IsDismissable)
        {
            // A notification the user cannot dismiss sits behind the ones they can, so it never blocks
            // them from reaching the rest.
            int blockingIndex = _notifications.FindIndex(entry => !entry.IsDismissable);
            if (blockingIndex >= 0)
            {
                insertIndex = blockingIndex;
            }
        }

        _notifications.Insert(insertIndex, notification);

        UpdateBanner();
    }

    /// <summary>
    /// Moves the banner on to the next notification, hiding it once none are left.
    /// </summary>
    public void OnBannerDismissed()
    {
        if (_currentIndex >= _notifications.Count)
        {
            return;
        }

        _currentIndex++;

        UpdateBanner();
    }

    public void OnActionClicked()
    {
        if (_currentIndex >= _notifications.Count)
        {
            return;
        }

        var notification = _notifications[_currentIndex];

        switch (notification.Action)
        {
            case NotificationAction.ReloadProject:
                _commandService.Execute<IReloadProjectCommand>();
                break;

            case NotificationAction.ViewReport:
                var reportResource = notification.ReportResource;
                _commandService.Execute<IOpenDocumentCommand>(command => command.FileResource = reportResource);
                break;
        }
    }

    private void UpdateBanner()
    {
        if (_currentIndex >= _notifications.Count)
        {
            IsBannerVisible = false;
            return;
        }

        var notification = _notifications[_currentIndex];

        BannerSeverity = ResolveBannerSeverity(notification.Severity);
        BannerTitle = notification.Title;
        BannerMessage = notification.Message;
        IsBannerDismissable = notification.IsDismissable;

        ActionText = ResolveActionText(notification.Action);
        IsActionVisible = notification.Action != NotificationAction.None;

        IsBannerVisible = true;
    }

    private string ResolveActionText(NotificationAction action)
    {
        switch (action)
        {
            case NotificationAction.ReloadProject:
                return _stringLocalizer.GetString("NotificationBar_ReloadProjectButton");

            case NotificationAction.ViewReport:
                return _stringLocalizer.GetString("NotificationBar_ViewReportButton");

            default:
                return string.Empty;
        }
    }

    private static InfoBarSeverity ResolveBannerSeverity(ReportSeverity severity)
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

    private void CheckMigrationStatus()
    {
        var currentProject = _projectService?.CurrentProject;
        if (currentProject == null)
        {
            return;
        }

        // Only show the migration banner if there was an actual version change
        var oldVersion = currentProject.MigrationResult.OldVersion;
        var newVersion = currentProject.MigrationResult.NewVersion;

        if (!string.IsNullOrEmpty(oldVersion) &&
            !string.IsNullOrEmpty(newVersion) &&
            oldVersion != newVersion)
        {
            var title = _stringLocalizer.GetString("NotificationBar_MigrationBannerTitle");
            var message = _stringLocalizer.GetString("NotificationBar_MigrationBannerMessage", oldVersion, newVersion);

            AddNotification(new WorkspaceNotification(ReportSeverity.Info, title, message));
        }
    }

    public void Cleanup()
    {
        _messengerService.UnregisterAll(this);
    }
}
