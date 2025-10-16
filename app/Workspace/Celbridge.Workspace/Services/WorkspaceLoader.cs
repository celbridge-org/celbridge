using Celbridge.Logging;

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
        var projectFolderPath = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry.ProjectFolderPath;
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
            var expandedFolders = await workspaceSettings.GetPropertyAsync<List<string>>("ExpandedFolders");
            if (expandedFolders is not null &&
                expandedFolders.Count > 0)
            {
                var resourceRegistry = workspaceService.ExplorerService.ResourceRegistry;
                foreach (var expandedFolder in expandedFolders)
                {
                    resourceRegistry.SetFolderIsExpanded(expandedFolder, true);
                }
            }

            var updateResult = await explorerService.UpdateResourcesAsync();
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

        var consoleService = workspaceService.ConsoleService;
        var initTerminal = await consoleService.InitializeTerminalWindow();
        if (initTerminal.IsFailure)
        {
            return Result.Fail("Failed to initialize console terminal")
                .WithErrors(initTerminal);
        }

        var pythonService = workspaceService.PythonService;
        var initPython = await pythonService.InitializePython();
        if (initPython.IsFailure)
        {
            return Result.Fail("Failed to initialize Python scripting")
                .WithErrors(initPython);
        }

        return Result.Ok();
    }
}
