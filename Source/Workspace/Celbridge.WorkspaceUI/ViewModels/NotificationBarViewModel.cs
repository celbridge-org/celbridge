using Celbridge.Commands;
using Celbridge.Projects;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;

namespace Celbridge.WorkspaceUI.ViewModels;

/// <summary>
/// Tracks project-scoped conditions worth telling the user about, such as a config file that failed
/// to load or has changed on disk, and exposes them as banners for the notification bar.
/// </summary>
public partial class NotificationBarViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly IDispatcher _dispatcher;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IProjectService _projectService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ICommandService _commandService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyBannerVisible))]
    private bool _isErrorBannerVisible;

    [ObservableProperty]
    private string _errorBannerTitle = string.Empty;

    [ObservableProperty]
    private string _errorBannerMessage = string.Empty;

    [ObservableProperty]
    private string _projectChangeBannerTitle = string.Empty;

    [ObservableProperty]
    private string _projectChangeBannerMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyBannerVisible))]
    private bool _isProjectChangeBannerVisible;

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

    public bool IsAnyBannerVisible =>
        IsErrorBannerVisible ||
        IsProjectChangeBannerVisible ||
        IsMigrationBannerVisible ||
        IsProjectCheckBannerVisible;

    private string? _originalProjectFileHash = null;

    public NotificationBarViewModel(
        IMessengerService messengerService,
        IDispatcher dispatcher,
        IStringLocalizer stringLocalizer,
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper,
        ICommandService commandService)
    {
        _messengerService = messengerService;
        _dispatcher = dispatcher;
        _stringLocalizer = stringLocalizer;
        _projectService = projectService;
        _workspaceWrapper = workspaceWrapper;
        _commandService = commandService;

        // Register for project error messages
        _messengerService.Register<ProjectErrorMessage>(this, OnProjectError);

        // Register for resource change messages to monitor project file changes
        _messengerService.Register<ResourceChangedMessage>(this, OnResourceChanged);

        // Snapshot the project file contents so subsequent changes can be
        // detected. The hash read goes through the file storage gateway,
        // which is async. Fire-and-forget here since the constructor is sync
        // and the snapshot is only consulted on later change events.
        _ = StoreProjectFileHashAsync();

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
                ProjectCheckBannerMessage = _stringLocalizer.GetString("NotificationBar_ProjectCheckFindingsMessage", configFile);
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

        // Hide project change banner when error banner is shown
        IsProjectChangeBannerVisible = false;
    }

    public void OnProjectCheckBannerClosed()
    {
        IsProjectCheckBannerVisible = false;
    }

    public void OnReloadProjectClicked()
    {
        _commandService.Execute<IReloadProjectCommand>();
    }

    private void OnResourceChanged(object recipient, ResourceChangedMessage message)
    {
        // Check if the changed resource is the .celbridge project file
        var projectFilePath = _projectService?.CurrentProject?.ProjectFilePath;
        if (string.IsNullOrEmpty(projectFilePath))
        {
            return;
        }

        var projectFileName = Path.GetFileName(projectFilePath);
        var changedResourcePath = message.Resource.Path;

        if (changedResourcePath.Equals(projectFileName, StringComparison.OrdinalIgnoreCase))
        {
            // This handler may be called from a background thread so ensure that the message
            // is handled on the main UI thread.
            _dispatcher.TryEnqueue(async () =>
            {
                await CheckProjectFileChangedAsync();
            });
        }
    }

    // Resolves the project config file as a ResourceKey at the project root.
    // The .celbridge config sits next to the project folder root, so its key
    // is just the file name on the default root.
    private bool TryGetProjectFileResourceKey(out ResourceKey resourceKey)
    {
        resourceKey = default;
        var projectFilePath = _projectService?.CurrentProject?.ProjectFilePath;
        if (string.IsNullOrEmpty(projectFilePath))
        {
            return false;
        }

        var projectFileName = Path.GetFileName(projectFilePath);
        return ResourceKey.TryCreate(projectFileName, out resourceKey);
    }

    private async Task StoreProjectFileHashAsync()
    {
        if (!TryGetProjectFileResourceKey(out var projectFileResource))
        {
            _originalProjectFileHash = null;
            return;
        }

        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
        var hashResult = await resourceFileSystem.ComputeHashAsync(projectFileResource);
        if (hashResult.IsFailure)
        {
            _originalProjectFileHash = null;
            return;
        }

        _originalProjectFileHash = hashResult.Value;
    }

    private async Task CheckProjectFileChangedAsync()
    {
        if (!TryGetProjectFileResourceKey(out var projectFileResource))
        {
            return;
        }

        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
        var hashResult = await resourceFileSystem.ComputeHashAsync(projectFileResource);
        if (hashResult.IsFailure)
        {
            // If we can't read the file, hide the banner
            IsProjectChangeBannerVisible = false;
            return;
        }

        var currentHash = hashResult.Value;

        // If error banner is visible, don't show the project change banner
        if (IsErrorBannerVisible)
        {
            IsProjectChangeBannerVisible = false;
            return;
        }

        // Check if the hash has changed from the original
        if (_originalProjectFileHash is null
            || !string.Equals(currentHash, _originalProjectFileHash, StringComparison.Ordinal))
        {
            // Populate the project change banner strings
            ProjectChangeBannerTitle = _stringLocalizer.GetString("NotificationBar_ProjectChangeBannerTitle");
            ProjectChangeBannerMessage = _stringLocalizer.GetString("NotificationBar_ProjectChangeBannerMessage");

            IsProjectChangeBannerVisible = true;
        }
        else
        {
            IsProjectChangeBannerVisible = false;
        }
    }

    public void OnProjectChangeBannerClosed()
    {
        IsProjectChangeBannerVisible = false;
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
