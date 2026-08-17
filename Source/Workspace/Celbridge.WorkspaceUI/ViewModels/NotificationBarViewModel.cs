using Celbridge.Commands;
using Celbridge.Projects;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;

namespace Celbridge.WorkspaceUI.ViewModels;

/// <summary>
/// Tracks project-scoped conditions worth telling the user about, such as a config file that failed
/// to load or was only partly applied, and exposes them as banners for the notification bar.
/// </summary>
public partial class NotificationBarViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly IDispatcher _dispatcher;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IProjectService _projectService;
    private readonly ICommandService _commandService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyBannerVisible))]
    private bool _isErrorBannerVisible;

    [ObservableProperty]
    private string _errorBannerTitle = string.Empty;

    [ObservableProperty]
    private string _errorBannerMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyBannerVisible))]
    private bool _isMigrationBannerVisible;

    [ObservableProperty]
    private string _migrationBannerTitle = string.Empty;

    [ObservableProperty]
    private string _migrationBannerMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyBannerVisible))]
    private bool _isProjectCheckBannerVisible;

    [ObservableProperty]
    private string _projectCheckBannerTitle = string.Empty;

    [ObservableProperty]
    private string _projectCheckBannerMessage = string.Empty;

    /// <summary>
    /// The report holding the detail behind the project check banner, or null when none was written.
    /// </summary>
    public ResourceKey? ProjectCheckReportResource { get; private set; }

    public bool IsAnyBannerVisible =>
        IsErrorBannerVisible ||
        IsMigrationBannerVisible ||
        IsProjectCheckBannerVisible;

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
            HandleProjectError(message);
        });
    }

    private void HandleProjectError(ProjectErrorMessage message)
    {
        var configFile = message.ConfigFileName ?? "project configuration file";

        // Set the error banner properties based on error type
        switch (message.ErrorType)
        {
            case ProjectErrorType.InvalidProjectConfig:
                ErrorBannerTitle = _stringLocalizer.GetString("NotificationBar_ProjectConfigErrorTitle");
                ErrorBannerMessage = _stringLocalizer.GetString("NotificationBar_ProjectConfigErrorMessage", configFile);
                break;

            case ProjectErrorType.IncompatibleVersion:
                ErrorBannerTitle = _stringLocalizer.GetString("NotificationBar_IncompatibleVersionTitle");
                ErrorBannerMessage = _stringLocalizer.GetString("NotificationBar_IncompatibleVersionMessage", configFile);
                break;

            case ProjectErrorType.InvalidVersion:
                ErrorBannerTitle = _stringLocalizer.GetString("NotificationBar_InvalidVersionTitle");
                ErrorBannerMessage = _stringLocalizer.GetString("NotificationBar_InvalidVersionMessage", configFile);
                break;

            case ProjectErrorType.MigrationError:
                ErrorBannerTitle = _stringLocalizer.GetString("NotificationBar_MigrationErrorTitle");
                ErrorBannerMessage = _stringLocalizer.GetString("NotificationBar_MigrationErrorMessage", configFile);
                break;

            case ProjectErrorType.PackageLoadError:
                ErrorBannerTitle = _stringLocalizer.GetString("NotificationBar_PackageLoadErrorTitle");
                ErrorBannerMessage = _stringLocalizer.GetString("NotificationBar_PackageLoadErrorMessage");
                break;

            case ProjectErrorType.ProjectCheckError:
                // Project check findings are advisory, not blocking — the
                // project loaded fine. Route to the dismissable warning
                // banner rather than the non-dismissable error banner, and
                // return early so the error-banner side effects below do
                // not fire.
                ProjectCheckBannerTitle = _stringLocalizer.GetString("NotificationBar_ProjectCheckFindingsTitle");
                ProjectCheckBannerMessage = _stringLocalizer.GetString("NotificationBar_ProjectCheckFindingsMessage", message.FindingCount);
                ProjectCheckReportResource = message.ReportResource;
                IsProjectCheckBannerVisible = true;
                return;

            case ProjectErrorType.ProjectConfigEntryError:
                // Per-entry config errors are advisory: the rest of the file
                // applied and the project loaded. Route to the dismissable
                // warning banner like project check findings.
                ProjectCheckBannerTitle = _stringLocalizer.GetString("NotificationBar_ProjectConfigEntryErrorTitle");
                ProjectCheckBannerMessage = _stringLocalizer.GetString("NotificationBar_ProjectConfigEntryErrorMessage", configFile);
                IsProjectCheckBannerVisible = true;
                return;

            default:
                throw new ArgumentOutOfRangeException();
        }

        IsErrorBannerVisible = true;
    }

    public void OnProjectCheckBannerClosed()
    {
        IsProjectCheckBannerVisible = false;
    }

    public void OnReloadProjectClicked()
    {
        _commandService.Execute<IReloadProjectCommand>();
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
            // Populate the migration banner strings
            MigrationBannerTitle = _stringLocalizer.GetString("NotificationBar_MigrationBannerTitle");
            MigrationBannerMessage = _stringLocalizer.GetString("NotificationBar_MigrationBannerMessage", oldVersion, newVersion);
            IsMigrationBannerVisible = true;
        }
    }

    public void OnMigrationBannerClosed()
    {
        IsMigrationBannerVisible = false;
    }

    public void Cleanup()
    {
        _messengerService.UnregisterAll(this);
    }
}
