using Celbridge.Automation;
using Celbridge.Dialog;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Settings;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Projects.Services;

/// <summary>
/// Handles the complete workflow of loading a project, including the checks that can refuse it, the project lock,
/// migration checks, upgrade confirmation dialogs, error alerts, and showing the workspace.
/// </summary>
internal class ProjectLoader : IProjectLoader
{
    private readonly ILogger<ProjectLoader> _logger;
    private readonly IProjectMigrationService _migrationService;
    private readonly IProjectService _projectService;
    private readonly IProjectLockService _projectLockService;
    private readonly ProjectUnloader _projectUnloader;
    private readonly IDialogService _dialogService;
    private readonly IApplicationShell _applicationShell;
    private readonly ISettingsService _settingsService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IMessengerService _messengerService;
    private readonly IAutomationService _automationService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IProjectLoadReporter _loadReporter;

    public ProjectLoader(
        ILogger<ProjectLoader> logger,
        IProjectMigrationService migrationService,
        IProjectService projectService,
        IProjectLockService projectLockService,
        ProjectUnloader projectUnloader,
        IDialogService dialogService,
        IApplicationShell applicationShell,
        ISettingsService settingsService,
        IWorkspaceWrapper workspaceWrapper,
        IMessengerService messengerService,
        IAutomationService automationService,
        IStringLocalizer stringLocalizer,
        IProjectLoadReporter loadReporter)
    {
        _logger = logger;
        _migrationService = migrationService;
        _projectService = projectService;
        _projectLockService = projectLockService;
        _projectUnloader = projectUnloader;
        _dialogService = dialogService;
        _applicationShell = applicationShell;
        _settingsService = settingsService;
        _workspaceWrapper = workspaceWrapper;
        _messengerService = messengerService;
        _automationService = automationService;
        _stringLocalizer = stringLocalizer;
        _loadReporter = loadReporter;
    }

    public async Task<Result> LoadProjectAsync(string projectFilePath, ProjectLoadOrigin origin)
    {
        // This instance takes the lock of every project it opens, so this project is already open here.
        if (_projectLockService.HoldsLock(projectFilePath))
        {
            return Result.Ok();
        }

        // The checks run before the current project closes, so a refused project leaves the current project open.
        var otherProjectFilePath = await FindOtherProjectFileAsync(projectFilePath);
        if (otherProjectFilePath is not null)
        {
            return await RefuseOtherProjectFileAsync(projectFilePath, otherProjectFilePath);
        }

        var lockTaken = await TakeLockAsync(projectFilePath);
        if (!lockTaken)
        {
            return await RefuseOpenProjectAsync(projectFilePath, origin);
        }

        // The load report goes in the project's data folder, which the lock now protects.
        _loadReporter.BeginLoad(projectFilePath);

        Result<bool> loadResult;
        try
        {
            loadResult = await LoadLockedProjectAsync(projectFilePath);
        }
        catch (Exception exception)
        {
            loadResult = Result<bool>.Fail($"Failed to load project: '{projectFilePath}'")
                .WithException(exception);
        }
        finally
        {
            await _loadReporter.FlushAsync();
        }

        // A load that ends without a workspace leaves the project closed.
        var workspaceLoaded = loadResult.IsSuccess && loadResult.Value;
        if (!workspaceLoaded)
        {
            _projectLockService.ReleaseLock(projectFilePath);
        }

        if (loadResult.IsFailure)
        {
            _automationService.LogEvent(new ProjectLoadFailedEvent(projectFilePath));
            return Result.Fail(loadResult);
        }

        return Result.Ok();
    }

    // Returns the path of a project file in the project's folder other than the one being opened, or null if
    // there is none.
    private async Task<string?> FindOtherProjectFileAsync(string projectFilePath)
    {
        var projectFolderPath = ProjectFileHelper.GetProjectFolderPath(projectFilePath);
        var projectFilesResult = await _projectService.GetProjectFilesAsync(projectFolderPath);
        if (projectFilesResult.IsFailure)
        {
            // The load goes ahead, and reports any problem reading the folder.
            _logger.LogWarning(projectFilesResult, $"Failed to check the project folder for other project files: '{projectFolderPath}'");
            return null;
        }

        return ProjectFileHelper.FindOtherProjectFile(projectFilePath, projectFilesResult.Value);
    }

    // Returns false if another instance holds the lock. A lock that fails for any other reason lets the load go
    // ahead without it, and the load reports any problem with the data folder.
    private async Task<bool> TakeLockAsync(string projectFilePath)
    {
        var lockResult = await _projectLockService.TryTakeLockAsync(projectFilePath);
        if (lockResult.IsFailure)
        {
            _logger.LogWarning(lockResult, $"Opening the project without its lock: '{projectFilePath}'");
            return true;
        }

        return lockResult.Value;
    }

    private async Task<Result> RefuseOtherProjectFileAsync(string projectFilePath, string otherProjectFilePath)
    {
        // The project can't open until the other project file is moved or deleted.
        ForgetPreviousProject(projectFilePath);

        var otherFileName = Path.GetFileName(otherProjectFilePath);
        var title = _stringLocalizer.GetString("AnotherProjectFileAlert_Title");
        var message = _stringLocalizer.GetString("AnotherProjectFileAlert_Message", otherFileName);
        await _dialogService.ShowAlertDialogAsync(title, message);

        _automationService.LogEvent(new ProjectLoadFailedEvent(projectFilePath));

        return Result.Fail($"Failed to load project because its folder contains another project file: '{otherProjectFilePath}'");
    }

    // Nothing is wrong with a project that another instance has open, so the previous-project setting stays.
    private async Task<Result> RefuseOpenProjectAsync(string projectFilePath, ProjectLoadOrigin origin)
    {
        if (origin == ProjectLoadOrigin.PreviousProject)
        {
            // The launch shows Home instead, without an alert.
            _automationService.LogEvent(new PreviousProjectSkippedEvent(projectFilePath));
            return Result.Ok();
        }

        var projectName = Path.GetFileNameWithoutExtension(projectFilePath);
        var title = _stringLocalizer.GetString("ProjectAlreadyOpenAlert_Title");
        var message = _stringLocalizer.GetString("ProjectAlreadyOpenAlert_Message", projectName);
        await _dialogService.ShowAlertDialogAsync(title, message);

        _automationService.LogEvent(new ProjectLoadFailedEvent(projectFilePath));

        if (origin == ProjectLoadOrigin.LaunchFile)
        {
            // The instance was started only to open this project. The exit queues its unload behind the running load
            // command.
            _ = _applicationShell.ExitApplicationAsync();
        }

        return Result.Fail($"Failed to load project because another instance has it open: '{projectFilePath}'");
    }

    // Runs with the project's lock held. Returns true once the workspace has loaded, and false when the user declines
    // the upgrade.
    private async Task<Result<bool>> LoadLockedProjectAsync(string projectFilePath)
    {
        var projectName = Path.GetFileNameWithoutExtension(projectFilePath);

        // Check the project's migration status
        var migrationResult = await _migrationService.CheckMigrationAsync(projectFilePath);

        // A version this build can't open, or a declined upgrade, ends the load before the current project closes.
        bool userConfirmedUpgrade = false;
        switch (migrationResult.Status)
        {
            case MigrationStatus.NewerCelbridgeVersion:
            case MigrationStatus.UnsupportedCelbridgeVersion:
            {
                // This version of Celbridge cannot open the project
                _logger.LogError($"Cannot load project '{projectName}' - Celbridge version v{migrationResult.OldVersion} is not supported by application version v{migrationResult.NewVersion}");
                ForgetPreviousProject(projectFilePath);

                _loadReporter.RecordMigrationResult(migrationResult, userConfirmedUpgrade: false, userCancelledUpgrade: false);
                _loadReporter.RecordLoadOutcome(loadSucceeded: false, loadResult: null);

                await ShowCelbridgeVersionAlertAsync(projectName, migrationResult);

                return Result<bool>.Fail($"Failed to load project: '{projectFilePath}'")
                    .WithErrors(migrationResult.OperationResult);
            }

            case MigrationStatus.UpgradeRequired:
            {
                var confirmed = await ShowUpgradeConfirmationDialogAsync(
                    projectName,
                    migrationResult.OldVersion,
                    migrationResult.NewVersion);

                if (!confirmed)
                {
                    _logger.LogInformation($"User cancelled project upgrade for '{projectName}'");
                    _loadReporter.RecordMigrationResult(migrationResult, userConfirmedUpgrade: false, userCancelledUpgrade: true);
                    _loadReporter.RecordLoadOutcome(loadSucceeded: false, loadResult: null);

                    // Not a failure. The user chose to keep the current project.
                    return false;
                }

                userConfirmedUpgrade = true;
                break;
            }
        }

        // Close any loaded project. A teardown that fails still closes the project, so the load goes on.
        var unloadResult = await _projectUnloader.UnloadProjectAsync();
        if (unloadResult.IsFailure)
        {
            _logger.LogWarning(unloadResult, "Failed to close the current project cleanly before loading another");
        }

        if (userConfirmedUpgrade)
        {
            _logger.LogInformation($"User confirmed upgrade for '{projectName}' from v{migrationResult.OldVersion} to v{migrationResult.NewVersion}");

            migrationResult = await _migrationService.PerformMigrationUpgradeAsync(projectFilePath);

            if (migrationResult.Status != MigrationStatus.Complete)
            {
                // Upgrade failed - show alert but continue to load with limited functionality
                _logger.LogWarning($"Project upgrade failed for '{projectName}', continuing with limited functionality");
                await ShowUpgradeFailedAlertAsync(projectName);
            }
        }
        else if (migrationResult.Status is MigrationStatus.InvalidConfig
            or MigrationStatus.InvalidVersion
            or MigrationStatus.Failed)
        {
            // Configuration error - show alert but continue to load with limited functionality
            _logger.LogWarning($"Project '{projectName}' has configuration errors, continuing with limited functionality");
            await ShowConfigErrorAlertAsync(projectName);
        }

        _loadReporter.RecordMigrationResult(migrationResult, userConfirmedUpgrade, userCancelledUpgrade: false);

        // Load the project and navigate to workspace
        var loadResult = await LoadProjectInternalAsync(projectFilePath, migrationResult);

        _loadReporter.RecordLoadOutcome(loadResult.IsSuccess, loadResult);

        if (loadResult.IsFailure)
        {
            _settingsService.Set(SettingCatalog.Project.PreviousProject, string.Empty);

            // The view may already have been created when the load failed, so take it back down. No project
            // is loaded, so the shell shows Home.
            var closeResult = await _applicationShell.CloseWorkspaceAsync();
            if (closeResult.IsFailure)
            {
                _logger.LogError(closeResult, "Failed to close the workspace after the project load failed");
            }

            await ShowLoadFailedAlertAsync(projectFilePath);

            return Result<bool>.Fail($"Failed to load project: '{projectFilePath}'")
                .WithErrors(loadResult);
        }

        _settingsService.Set(SettingCatalog.Project.PreviousProject, projectFilePath);

        return true;
    }

    // A refused project can't open as it stands, so the next launch skips it. The setting is cleared only when it
    // names this project, since the current project stays open.
    private void ForgetPreviousProject(string projectFilePath)
    {
        var previousProject = _settingsService.Get(SettingCatalog.Project.PreviousProject);
        if (string.Equals(previousProject, projectFilePath, PathComparison.Comparison))
        {
            _settingsService.Set(SettingCatalog.Project.PreviousProject, string.Empty);
        }
    }

    private async Task<bool> ShowUpgradeConfirmationDialogAsync(string projectName, string oldVersion, string newVersion)
    {
        var title = _stringLocalizer.GetString("ProjectUpgradeConfirmation_Title");
        var message = _stringLocalizer.GetString("ProjectUpgradeConfirmation_Message", projectName, oldVersion, newVersion);

        var confirmResult = await _dialogService.ShowConfirmationDialogAsync(title, message);

        if (confirmResult.IsFailure)
        {
            return false;
        }

        return confirmResult.Value;
    }

    private async Task ShowUpgradeFailedAlertAsync(string projectName)
    {
        var title = _stringLocalizer.GetString("ProjectUpgradeFailedAlert_Title");
        var message = _stringLocalizer.GetString("ProjectUpgradeFailedAlert_Message", projectName);
        await _dialogService.ShowAlertDialogAsync(title, message);
    }

    private async Task ShowConfigErrorAlertAsync(string projectName)
    {
        var title = _stringLocalizer.GetString("ProjectConfigErrorAlert_Title");
        var message = _stringLocalizer.GetString("ProjectConfigErrorAlert_Message", projectName);
        await _dialogService.ShowAlertDialogAsync(title, message);
    }

    private async Task ShowCelbridgeVersionAlertAsync(string projectName, MigrationResult migrationResult)
    {
        string title;
        string message;

        if (migrationResult.Status == MigrationStatus.NewerCelbridgeVersion)
        {
            title = _stringLocalizer.GetString("ProjectNewerVersionAlert_Title");
            message = _stringLocalizer.GetString("ProjectNewerVersionAlert_Message", projectName, migrationResult.OldVersion, migrationResult.NewVersion);
        }
        else
        {
            title = _stringLocalizer.GetString("ProjectUnsupportedVersionAlert_Title");
            message = _stringLocalizer.GetString("ProjectUnsupportedVersionAlert_Message", projectName, migrationResult.OldVersion, ProjectConstants.MinimumSupportedCelbridgeVersion);
        }

        await _dialogService.ShowAlertDialogAsync(title, message);
    }

    private async Task ShowLoadFailedAlertAsync(string projectFilePath)
    {
        var title = _stringLocalizer.GetString("LoadProjectFailedAlert_Title");
        var message = _stringLocalizer.GetString("LoadProjectFailedAlert_Message", projectFilePath);
        await _dialogService.ShowAlertDialogAsync(title, message);
    }

    private async Task<Result> LoadProjectInternalAsync(string projectFilePath, MigrationResult migrationResult)
    {
        var loadResult = await _projectService.LoadProjectAsync(projectFilePath, migrationResult);
        if (loadResult.IsFailure)
        {
            return Result.Fail($"Failed to open project file '{projectFilePath}'")
                .WithErrors(loadResult);
        }

        var loadPageCancellationToken = new CancellationTokenSource();

        // Use TaskCompletionSource for event-based waiting instead of polling
        var workspaceLoadedTcs = new TaskCompletionSource<bool>();

        void OnWorkspaceLoaded(object recipient, WorkspaceLoadedMessage message)
        {
            workspaceLoadedTcs.TrySetResult(true);
        }

        // Registered before the view is created, so a load that completes while this method is still
        // running cannot be missed.
        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoaded);

        try
        {
            var showResult = await _applicationShell.ShowWorkspaceAsync(loadPageCancellationToken);
            if (showResult.IsFailure)
            {
                return Result.Fail("Failed to show the workspace")
                    .WithErrors(showResult);
            }

            // If already loaded, complete immediately
            if (_workspaceWrapper.IsWorkspaceLoaded)
            {
                return Result.Ok();
            }

            // Wait for either workspace load completion or cancellation
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(loadPageCancellationToken.Token);

            var completedTask = await Task.WhenAny(
                workspaceLoadedTcs.Task,
                Task.Delay(Timeout.Infinite, linkedCts.Token));

            if (loadPageCancellationToken.IsCancellationRequested)
            {
                return Result.Fail("Failed to open project because an error occurred");
            }

            return Result.Ok();
        }
        finally
        {
            _messengerService.Unregister<WorkspaceLoadedMessage>(this);
        }
    }
}
