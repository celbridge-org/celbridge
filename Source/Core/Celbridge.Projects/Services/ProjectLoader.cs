using Celbridge.Dialog;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Navigation;
using Celbridge.Settings;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Projects.Services;

/// <summary>
/// Handles the complete workflow of loading a project, including migration checks,
/// upgrade confirmation dialogs, error alerts, and navigation.
/// </summary>
public class ProjectLoader : IProjectLoader
{
    private readonly ILogger<ProjectLoader> _logger;
    private readonly IProjectMigrationService _migrationService;
    private readonly IProjectService _projectService;
    private readonly IDialogService _dialogService;
    private readonly INavigationService _navigationService;
    private readonly IEditorSettings _editorSettings;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;

    public ProjectLoader(
        ILogger<ProjectLoader> logger,
        IProjectMigrationService migrationService,
        IProjectService projectService,
        IDialogService dialogService,
        INavigationService navigationService,
        IEditorSettings editorSettings,
        IWorkspaceWrapper workspaceWrapper,
        IMessengerService messengerService,
        IStringLocalizer stringLocalizer)
    {
        _logger = logger;
        _migrationService = migrationService;
        _projectService = projectService;
        _dialogService = dialogService;
        _navigationService = navigationService;
        _editorSettings = editorSettings;
        _workspaceWrapper = workspaceWrapper;
        _messengerService = messengerService;
        _stringLocalizer = stringLocalizer;
    }

    /// <summary>
    /// Loads a project with full migration support, user dialogs, and navigation.
    /// Handles the complete workflow including upgrade confirmation, error alerts,
    /// and navigating to the workspace or home page as appropriate.
    /// </summary>
    public async Task<Result> LoadProjectAsync(string projectFilePath)
    {
        var projectName = Path.GetFileNameWithoutExtension(projectFilePath);

        // Check the project's migration status
        var migrationResult = await _migrationService.CheckMigrationAsync(projectFilePath);

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
                    _logger.LogInformation("User cancelled project upgrade for '{ProjectName}'", projectName);
                    _navigationService.NavigateToPage(NavigationConstants.HomeTag);
                    return Result.Ok(); // Not a failure - user chose to cancel
                }

                // User confirmed - perform the upgrade
                _logger.LogInformation("User confirmed upgrade for '{ProjectName}' from v{OldVersion} to v{NewVersion}",
                    projectName, migrationResult.OldVersion, migrationResult.NewVersion);

                migrationResult = await _migrationService.PerformMigrationUpgradeAsync(projectFilePath);

                if (migrationResult.Status != MigrationStatus.Complete)
                {
                    // Upgrade failed - show alert but continue to load with limited functionality
                    _logger.LogWarning("Project upgrade failed for '{ProjectName}', continuing with limited functionality", projectName);
                    await ShowUpgradeFailedAlertAsync(projectName);
                }
                break;
            }

            case MigrationStatus.IncompatibleVersion:
            {
                // Project was created with a newer version of Celbridge - cannot load
                _logger.LogError("Cannot load project '{ProjectName}' - created with newer Celbridge version", projectName);
                _editorSettings.PreviousProject = string.Empty;

                await ShowLoadFailedAlertAsync(projectFilePath);
                _navigationService.NavigateToPage(NavigationConstants.HomeTag);

                return Result.Fail($"Failed to load project: '{projectFilePath}'")
                    .WithErrors(migrationResult.OperationResult);
            }

            case MigrationStatus.InvalidConfig:
            case MigrationStatus.InvalidVersion:
            case MigrationStatus.Failed:
            {
                // Configuration error - show alert but continue to load with limited functionality
                _logger.LogWarning("Project '{ProjectName}' has configuration errors, continuing with limited functionality", projectName);
                await ShowConfigErrorAlertAsync(projectName);
                break;
            }
        }

        // Load the project and navigate to workspace
        var loadResult = await LoadProjectInternalAsync(projectFilePath, migrationResult);

        if (loadResult.IsFailure)
        {
            _editorSettings.PreviousProject = string.Empty;

            await ShowLoadFailedAlertAsync(projectFilePath);
            _navigationService.NavigateToPage(NavigationConstants.HomeTag);

            return Result.Fail($"Failed to load project: '{projectFilePath}'")
                .WithErrors(loadResult);
        }

        _editorSettings.PreviousProject = projectFilePath;

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
        _navigationService.NavigateToPage(NavigationConstants.WorkspaceTag, loadPageCancellationToken);

        // Use TaskCompletionSource for event-based waiting instead of polling
        var workspaceLoadedTcs = new TaskCompletionSource<bool>();

        void OnWorkspaceLoaded(object recipient, WorkspaceLoadedMessage message)
        {
            workspaceLoadedTcs.TrySetResult(true);
        }

        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoaded);

        try
        {
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
