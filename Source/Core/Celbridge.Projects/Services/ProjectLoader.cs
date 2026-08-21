using Celbridge.Dialog;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Settings;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Projects.Services;

/// <summary>
/// Handles the complete workflow of loading a project, including migration checks,
/// upgrade confirmation dialogs, error alerts, and showing the workspace.
/// </summary>
public class ProjectLoader : IProjectLoader
{
    private readonly ILogger<ProjectLoader> _logger;
    private readonly IProjectMigrationService _migrationService;
    private readonly IProjectService _projectService;
    private readonly IDialogService _dialogService;
    private readonly IApplicationShell _applicationShell;
    private readonly ISettingsService _settingsService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IProjectLoadReporter _loadReporter;

    public ProjectLoader(
        ILogger<ProjectLoader> logger,
        IProjectMigrationService migrationService,
        IProjectService projectService,
        IDialogService dialogService,
        IApplicationShell applicationShell,
        ISettingsService settingsService,
        IWorkspaceWrapper workspaceWrapper,
        IMessengerService messengerService,
        IStringLocalizer stringLocalizer,
        IProjectLoadReporter loadReporter)
    {
        _logger = logger;
        _migrationService = migrationService;
        _projectService = projectService;
        _dialogService = dialogService;
        _applicationShell = applicationShell;
        _settingsService = settingsService;
        _workspaceWrapper = workspaceWrapper;
        _messengerService = messengerService;
        _stringLocalizer = stringLocalizer;
        _loadReporter = loadReporter;
    }

    /// <summary>
    /// Loads a project with full migration support, user dialogs, and error alerts. Shows the workspace on
    /// success; a load that does not happen leaves the shell showing Home.
    /// </summary>
    public async Task<Result> LoadProjectAsync(string projectFilePath)
    {
        _loadReporter.BeginLoad(projectFilePath);

        try
        {
            return await LoadProjectInnerAsync(projectFilePath);
        }
        finally
        {
            await _loadReporter.FlushAsync();
        }
    }

    private async Task<Result> LoadProjectInnerAsync(string projectFilePath)
    {
        var projectName = Path.GetFileNameWithoutExtension(projectFilePath);

        // Check the project's migration status
        var migrationResult = await _migrationService.CheckMigrationAsync(projectFilePath);

        bool userConfirmedUpgrade = false;
        bool userCancelledUpgrade = false;

        // Handle the various migration statuses
        switch (migrationResult.Status)
        {
            case MigrationStatus.Complete:
                // No upgrade needed - proceed to load
                break;

            case MigrationStatus.UpgradeRequired:
            {
                // Ask user for confirmation
                var confirmed = await ShowUpgradeConfirmationDialogAsync(
                    projectName,
                    migrationResult.OldVersion,
                    migrationResult.NewVersion);

                if (!confirmed)
                {
                    userCancelledUpgrade = true;
                    _logger.LogInformation($"User cancelled project upgrade for '{projectName}'");
                    _loadReporter.RecordMigrationResult(migrationResult, userConfirmedUpgrade: false, userCancelledUpgrade: true);
                    _loadReporter.RecordLoadOutcome(loadSucceeded: false, loadResult: null);
                    return Result.Ok(); // Not a failure - user chose to cancel
                }

                userConfirmedUpgrade = true;
                // User confirmed - perform the upgrade
                _logger.LogInformation($"User confirmed upgrade for '{projectName}' from v{migrationResult.OldVersion} to v{migrationResult.NewVersion}");

                migrationResult = await _migrationService.PerformMigrationUpgradeAsync(projectFilePath);

                if (migrationResult.Status != MigrationStatus.Complete)
                {
                    // Upgrade failed - show alert but continue to load with limited functionality
                    _logger.LogWarning($"Project upgrade failed for '{projectName}', continuing with limited functionality");
                    await ShowUpgradeFailedAlertAsync(projectName);
                }
                break;
            }

            case MigrationStatus.IncompatibleVersion:
            {
                // Project was created with a newer version of Celbridge - cannot load
                _logger.LogError($"Cannot load project '{projectName}' - created with newer Celbridge version");
                _settingsService.Set(SettingCatalog.Project.PreviousProject, string.Empty);

                _loadReporter.RecordMigrationResult(migrationResult, userConfirmedUpgrade: false, userCancelledUpgrade: false);
                _loadReporter.RecordLoadOutcome(loadSucceeded: false, loadResult: null);

                await ShowLoadFailedAlertAsync(projectFilePath);

                return Result.Fail($"Failed to load project: '{projectFilePath}'")
                    .WithErrors(migrationResult.OperationResult);
            }

            case MigrationStatus.InvalidConfig:
            case MigrationStatus.InvalidVersion:
            case MigrationStatus.Failed:
            {
                // Configuration error - show alert but continue to load with limited functionality
                _logger.LogWarning($"Project '{projectName}' has configuration errors, continuing with limited functionality");
                await ShowConfigErrorAlertAsync(projectName);
                break;
            }
        }

        _loadReporter.RecordMigrationResult(migrationResult, userConfirmedUpgrade, userCancelledUpgrade);

        // Load the project and navigate to workspace
        var loadResult = await LoadProjectInternalAsync(projectFilePath, migrationResult);

        _loadReporter.RecordLoadOutcome(loadResult.IsSuccess, loadResult);

        if (loadResult.IsFailure)
        {
            _settingsService.Set(SettingCatalog.Project.PreviousProject, string.Empty);

            // The view may already have been created when the load failed, so take it back down. No project
            // is loaded, so the shell shows Home.
            await _applicationShell.CloseWorkspaceAsync();

            await ShowLoadFailedAlertAsync(projectFilePath);

            return Result.Fail($"Failed to load project: '{projectFilePath}'")
                .WithErrors(loadResult);
        }

        _settingsService.Set(SettingCatalog.Project.PreviousProject, projectFilePath);

        return Result.Ok();
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
            if (_workspaceWrapper.IsWorkspacePageLoaded)
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
