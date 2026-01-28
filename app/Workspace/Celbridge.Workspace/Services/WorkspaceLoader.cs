using System.Text;
using Celbridge.Console;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Projects;

namespace Celbridge.Workspace.Services;

public class WorkspaceLoader
{
    private readonly ILogger<WorkspaceLoader> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public WorkspaceLoader(
        ILogger<WorkspaceLoader> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
    }

    public async Task<Result> LoadWorkspaceAsync()
    {
        var workspaceService = _workspaceWrapper.WorkspaceService;
        if (workspaceService is null)
        {
            return Result.Fail("Workspace service is not initialized");
        }

        //
        // Set the current directory to the workspace project folder
        //
        var projectFolderPath = _workspaceWrapper.WorkspaceService.ResourceService.Registry.ProjectFolderPath;
        projectFolderPath = Path.GetFullPath(projectFolderPath);
        if (Path.Exists(projectFolderPath))
        {
            Directory.SetCurrentDirectory(projectFolderPath);
        }

        //
        // Acquire the workspace settings
        //
        var workspaceSettingsService = workspaceService.WorkspaceSettingsService;
        var acquireResult = await workspaceSettingsService.AcquireWorkspaceSettingsAsync();
        if (acquireResult.IsFailure)
        {
            return Result.Fail("Failed to acquire the workspace settings")
                .WithErrors(acquireResult);
        }

        var workspaceSettings = workspaceSettingsService.WorkspaceSettings;
        Guard.IsNotNull(workspaceSettings);

        //
        // Initialize the entity service.
        //
        var entityService = workspaceService.EntityService;
        var initEntitiesResult = await entityService.InitializeAsync();
        if (initEntitiesResult.IsFailure)
        {
            return Result.Fail("Failed to initalize entity service")
                .WithErrors(initEntitiesResult);
        }

        //
        // Populate the resource registry.
        //

        var explorerService = workspaceService.ExplorerService;

        try
        {
            // Restore previous state of expanded folders before populating resources
            var resourceService = workspaceService.ResourceService;
            var expandedFolders = await workspaceSettings.GetPropertyAsync<List<string>>("ExpandedFolders");
            if (expandedFolders is not null &&
                expandedFolders.Count > 0)
            {
                var resourceRegistry = workspaceService.ResourceService.Registry;
                foreach (var expandedFolder in expandedFolders)
                {
                    resourceRegistry.SetFolderIsExpanded(expandedFolder, true);
                }
            }

            // Update resources registry immediately to ensure we are up to date
            var updateResult = resourceService.UpdateResources();
            if (updateResult.IsFailure)
            {
                return Result.Fail("Failed to update resources")
                    .WithErrors(updateResult);
            }
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred while populating the resource registry")
                .WithException(ex);
        }

        //
        // Initialize the activities service
        //

        var activityService = workspaceService.ActivityService;
        var initActivities = await activityService.Initialize();
        if (initActivities.IsFailure)
        {
            return Result.Fail("Failed to initialize activity service")
                .WithErrors(initActivities);
        }

        //
        // Restore the previous state of the workspace.
        // Any failures that occur here are logged as warnings and do not prevent the workspace from loading.
        //

        // Select the previous selected resource in the Explorer Panel.
        await explorerService.RestorePanelState();

        // Open previous opened documents in the Documents Panel
        var documentsService = workspaceService.DocumentsService;
        await documentsService.RestorePanelState();

        //
        // Update the current stored state of the workspace in preparation for the next session.
        //
        await explorerService.StoreSelectedResource();
        await documentsService.StoreSelectedDocument();
        await documentsService.StoreOpenDocuments();

        //
        // Initialize terminal window and Python scripting
        //

        // Workspace loading should continue even if terminal initialization fails

        var consoleService = workspaceService.ConsoleService;
        var initTerminal = await consoleService.InitializeTerminalWindow();
        if (initTerminal.IsFailure)
        {
            _logger.LogError(initTerminal.FirstException, "Failed to initialize console terminal: {Error}", initTerminal.Error);
        }

        // Initialize Python scripting
        // If Python fails to initialize, the error is reported and the project continues to load.
        await TryInitializePythonAsync(workspaceService);

        return Result.Ok();
    }

    private async Task TryInitializePythonAsync(IWorkspaceService workspaceService)
    {
        var projectService = ServiceLocator.AcquireService<IProjectService>();
        var currentProject = projectService.CurrentProject;

        if (currentProject is null)
        {
            return;
        }

        var migrationResult = currentProject.MigrationResult;

        if (migrationResult.Status != MigrationStatus.Complete)
        {
            HandleMigrationFailure(migrationResult, currentProject.ProjectFilePath);
            return;
        }

        var shortcutsSection = currentProject.ProjectConfig.Config.Shortcuts;
        if (shortcutsSection.HasErrors)
        {
            // Log the detailed errors but don't prevent workspace loading
            // The Python REPL will not be available until errors are fixed
            HandleShortcutConfigErrors(shortcutsSection.ValidationErrors, currentProject.ProjectFilePath);
            return;
        }

        // Project has loaded and migration completed with no config errors.
        // We can now safely initialize Python.
        var pythonService = workspaceService.PythonService;
        var pythonResult = await pythonService.InitializePython();
        if (pythonResult.IsFailure)
        {
            _logger.LogError(pythonResult, "Failed to initialize Python scripting");
        }
    }

    private void HandleShortcutConfigErrors(IReadOnlyList<ShortcutValidationError> errors, string projectFilePath)
    {
        var projectFileName = Path.GetFileName(projectFilePath);
        var messengerService = ServiceLocator.AcquireService<IMessengerService>();

        // Log detailed error information
        var sb = new StringBuilder();
        sb.AppendLine($"Shortcut configuration errors in '{projectFileName}' - Python initialization disabled:");
        foreach (var error in errors)
        {
            sb.AppendLine($"  Shortcut #{error.ShortcutIndex} ({error.PropertyName}): {error.Message}");
        }
        _logger.LogError(sb.ToString());

        var message = new ConsoleErrorMessage(ConsoleErrorType.ShortcutConfigError, projectFileName);
        messengerService.Send(message);
    }

    private void HandleMigrationFailure(MigrationResult migrationResult, string projectFilePath)
    {
        var projectFileName = Path.GetFileName(projectFilePath);
        var messengerService = ServiceLocator.AcquireService<IMessengerService>();

        ConsoleErrorMessage message;

        switch (migrationResult.Status)
        {
            case MigrationStatus.InvalidConfig:
                _logger.LogError("Project config is invalid - Python initialization disabled");
                message = new ConsoleErrorMessage(ConsoleErrorType.InvalidProjectConfig, projectFileName);
                messengerService.Send(message);
                break;

            case MigrationStatus.IncompatibleVersion:
                _logger.LogError("Project version is not compatible with application version - Python initialization disabled");
                message = new ConsoleErrorMessage(ConsoleErrorType.IncompatibleVersion, projectFileName);
                messengerService.Send(message);
                break;

            case MigrationStatus.InvalidVersion:
                _logger.LogError("Project version is invalid - Python initialization disabled");
                message = new ConsoleErrorMessage(ConsoleErrorType.InvalidVersion, projectFileName);
                messengerService.Send(message);
                break;

            case MigrationStatus.Failed:
                _logger.LogError("Project migration failed - Python initialization disabled");
                message = new ConsoleErrorMessage(ConsoleErrorType.MigrationError, projectFileName);
                messengerService.Send(message);
                break;
        }
    }
}
