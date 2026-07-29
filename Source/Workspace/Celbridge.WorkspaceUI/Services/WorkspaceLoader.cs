using System.Text;
using Celbridge.Logging;
using Celbridge.Packages;
using Celbridge.Platform;
using Celbridge.Projects;
using Celbridge.Server;
using Celbridge.Settings;

namespace Celbridge.WorkspaceUI.Services;

public class WorkspaceLoader
{
    private readonly ILogger<WorkspaceLoader> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IFeatureFlags _featureFlags;
    private readonly IProjectService _projectService;
    private readonly IServerService _serverService;
    private readonly ProjectCheckReporter _projectCheckReporter;
    private readonly IProjectLoadReporter _loadReporter;
    private readonly IAppEnvironment _appEnvironment;

    public WorkspaceLoader(
        ILogger<WorkspaceLoader> logger,
        IWorkspaceWrapper workspaceWrapper,
        IFeatureFlags featureFlags,
        IProjectService projectService,
        IServerService serverService,
        ProjectCheckReporter projectCheckReporter,
        IProjectLoadReporter loadReporter,
        IAppEnvironment appEnvironment)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
        _featureFlags = featureFlags;
        _projectService = projectService;
        _serverService = serverService;
        _projectCheckReporter = projectCheckReporter;
        _loadReporter = loadReporter;
        _appEnvironment = appEnvironment;
    }

    public async Task<Result> LoadWorkspaceAsync()
    {
        var workspaceService = _workspaceWrapper.WorkspaceService;
        if (workspaceService is null)
        {
            return Result.Fail("Workspace service is not initialized");
        }

        // Apply project-level feature flag overrides.
        var currentProject = _projectService.CurrentProject;
        if (currentProject is not null)
        {
            var projectFeatures = currentProject.Config.Features;
            _featureFlags.ApplyProjectOverrides(projectFeatures);

            // Surface entries the config parser skipped or degraded. The rest of the file applied.
            HandleConfigEntryErrors(currentProject.Config.EntryErrors, currentProject.ProjectFilePath);

            // Surface a failed migration or an invalid/incompatible project version as a banner. These are
            // project-scoped and fire on load regardless of whether any console is open.
            if (currentProject.MigrationResult.Status != MigrationStatus.Complete)
            {
                HandleMigrationFailure(currentProject.MigrationResult, currentProject.ProjectFilePath);
            }
        }

        // Start a fresh server instance for this workspace. The same port is reused for the lifetime of
        // the application so URLs resolved by the file server remain stable across project switches.
        await _serverService.StartAsync();
        if (_serverService.Status == ServerStatus.Error)
        {
            return Result.Fail("Failed to start the server for the workspace");
        }

        // Set the current directory to the workspace project folder.
        var projectFolderPath = _workspaceWrapper.WorkspaceService.ResourceService.Registry.ProjectFolderPath;
        projectFolderPath = Path.GetFullPath(projectFolderPath);
        SetProcessWorkingFolder(projectFolderPath);

        // Acquire the workspace settings.
        var workspaceSettingsService = workspaceService.WorkspaceSettings;
        var acquireResult = await workspaceSettingsService.AcquireWorkspaceSettingsAsync();
        if (acquireResult.IsFailure)
        {
            return Result.Fail("Failed to acquire the workspace settings")
                .WithErrors(acquireResult);
        }

        var propertyBag = workspaceSettingsService.PropertyBag;
        Guard.IsNotNull(propertyBag);

        // Initialize the entity service.
        var entityService = workspaceService.EntityService;
        var initEntitiesResult = await entityService.InitializeAsync();
        if (initEntitiesResult.IsFailure)
        {
            return Result.Fail("Failed to initalize entity service")
                .WithErrors(initEntitiesResult);
        }

        // Populate the resource registry.
        var explorerService = workspaceService.ExplorerService;
        var folderStateService = explorerService.FolderStateService;

        try
        {
            // Restore previous state of expanded folders before populating resources
            await folderStateService.LoadAsync();

            var resourceService = workspaceService.ResourceService;

            // Initialize the resource policy before the monitor, package scan, and
            // first registry build, each of which consults the policy engine.
            var initPolicyResult = await resourceService.Policy.InitializeAsync();

            // InitializeAsync degrades a missing or unreadable ignore-file to an
            // empty ignore set, so it does not currently fail.
            if (initPolicyResult.IsFailure)
            {
                _logger.LogWarning(initPolicyResult, "Failed to initialize resource policy");
            }

            // Start file system watchers now that the wrapper is fully populated.
            // The monitor cannot be initialized in ResourceService's constructor because
            // it reaches into the workspace via IWorkspaceWrapper, which is only set up
            // once construction completes.
            var initMonitorResult = resourceService.Monitor.Initialize();
            if (initMonitorResult.IsFailure)
            {
                _logger.LogWarning(initMonitorResult, "Failed to initialize resource monitor");
            }

            // Register packages before the first resource scan so the sidecar
            // pairing pass sees package-contributed document-editor factories.
            try
            {
                var packageService = workspaceService.PackageService;
                await packageService.RegisterPackagesAsync(projectFolderPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "An exception occurred while registering packages. The workspace will continue to load with reduced functionality.");
            }

            // Update resource registry immediately to ensure we are up to date
            var updateResult = await resourceService.UpdateResourcesAsync();
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

        // Initialize the activities service.
        var activityService = workspaceService.ActivityService;
        var initActivities = await activityService.Initialize();
        if (initActivities.IsFailure)
        {
            return Result.Fail("Failed to initialize activity service")
                .WithErrors(initActivities);
        }

        // Restore the previous state of the workspace. Any failures that occur here are logged as
        // warnings and do not prevent the workspace from loading.

        // Select the previous selected resources in the Explorer Panel.
        await explorerService.RestorePanelState();

        // Create a persistent surface for every utility and build their rail. This runs before the documents are
        // restored so a utility that was docked as a document last session is reparented into its saved tab
        // rather than opened as a second instance.
        await BuildUtilities();

        // Open previously opened documents in the Documents Panel. A stored utils: entry docks its
        // already-created utility into the saved tab position.
        var documentsService = workspaceService.DocumentsService;
        await documentsService.RestorePanelState();

        // Restore the previously selected Utility Panel tab, after documents are restored so a persisted surface
        // that ended up docked falls back to Explorer rather than showing an empty panel.
        _workspaceWrapper.WorkspaceService.UtilityPanel.RestoreSelectedUtility();

        // Update the current stored state of the workspace in preparation for the next session. Runs after the
        // dock restore so the re-persisted layout still records the docked utilities.
        await explorerService.StoreSelectedResources();
        await documentsService.StoreActiveDocument();
        await documentsService.StoreDocumentLayout();

        // Notify that the workspace has loaded.
        var messengerService = ServiceLocator.AcquireService<IMessengerService>();
        var workspaceLoadedMessage = new WorkspaceLoadedMessage();
        messengerService.Send(workspaceLoadedMessage);

        // A console session starts only when the user opens a .console document, not on load.

        // Awaited so the consistency check completes before any project script that runs on load can
        // modify the structure the scan reads.
        await RunProjectCheckAsync();

        return Result.Ok();
    }

    // Sets the process working folder to the loaded project. Directory.SetCurrentDirectory
    // sets process-global state that the ILocalFileSystem gateway does not model, so this
    // stays a direct System.IO carve-out.
    [AllowDirectFileSystemAccess]
    private static void SetProcessWorkingFolder(string folderPath)
    {
        if (Path.Exists(folderPath))
        {
            Directory.SetCurrentDirectory(folderPath);
        }
    }

    // Reverts the process working folder to the one captured at startup. Called when a project unloads so the
    // working folder stays valid while no project is loaded. A deleted project folder would otherwise leave
    // the working folder dangling, which breaks the next project's server start (getcwd fails).
    [AllowDirectFileSystemAccess]
    public void ResetProcessWorkingFolder()
    {
        var launchWorkingFolderPath = _appEnvironment.LaunchWorkingFolderPath;
        if (Path.Exists(launchWorkingFolderPath))
        {
            Directory.SetCurrentDirectory(launchWorkingFolderPath);
        }
    }

    // Errors are logged, never thrown — a broken consistency check must not fail
    // workspace load.
    private async Task RunProjectCheckAsync()
    {
        try
        {
            var commandService = ServiceLocator.AcquireService<Celbridge.Commands.ICommandService>();

            // ExecuteImmediate, not ExecuteAsync: this runs inside the in-flight LoadProjectCommand, so
            // enqueuing and awaiting a command would deadlock the serial queue.
            var reportResult = await commandService.ExecuteImmediate<IProjectCheckCommand, ProjectCheckReport>();
            if (reportResult.IsFailure)
            {
                _logger.LogWarning(reportResult, "Project consistency check failed.");
                return;
            }

            _projectCheckReporter.Report(reportResult.Value);
            _loadReporter.RecordCheckReport(reportResult.Value);
            await _loadReporter.FlushAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Project consistency check threw an unexpected exception.");
        }
    }

    // Creates the persistent surface for every utility instance and builds the rail. The utilities are owned by
    // the utility service for the workspace lifetime.
    private async Task BuildUtilities()
    {
        var utilityInstances = GetUtilityInstances();
        if (utilityInstances.Count == 0)
        {
            return;
        }

        var utilityService = _workspaceWrapper.WorkspaceService.UtilityService;
        var tabs = await utilityService.CreateUtilitiesAsync(utilityInstances);
        if (tabs.Count == 0)
        {
            return;
        }

        _workspaceWrapper.WorkspaceService.UtilityPanel.BuildCustomUtilities(tabs);
    }

    // Enumerates the declared utility instances. Declaration order in the project config is the
    // rail order.
    private List<ResolvedEditor> GetUtilityInstances()
    {
        var packageService = _workspaceWrapper.WorkspaceService.PackageService;

        var utilityInstances = new List<ResolvedEditor>();
        foreach (var instance in packageService.GetResolvedEditors())
        {
            if (!instance.Contribution.IsUtility)
            {
                continue;
            }

            utilityInstances.Add(instance);
        }

        return utilityInstances;
    }

    private void HandleConfigEntryErrors(IReadOnlyList<ProjectConfigEntryError> entryErrors, string projectFilePath)
    {
        if (entryErrors.Count == 0)
        {
            return;
        }

        var projectFileName = Path.GetFileName(projectFilePath);

        var sb = new StringBuilder();
        sb.AppendLine($"Project config entries in '{projectFileName}' were skipped or degraded:");
        foreach (var entryError in entryErrors)
        {
            sb.AppendLine($"  [{entryError.EntryName}]: {entryError.Message}");
        }
        _logger.LogError(sb.ToString());

        var messengerService = ServiceLocator.AcquireService<IMessengerService>();
        var message = new ProjectErrorMessage(ProjectErrorType.ProjectConfigEntryError, projectFileName);
        messengerService.Send(message);
    }

    private void HandleMigrationFailure(MigrationResult migrationResult, string projectFilePath)
    {
        var projectFileName = Path.GetFileName(projectFilePath);
        var messengerService = ServiceLocator.AcquireService<IMessengerService>();

        ProjectErrorMessage message;

        switch (migrationResult.Status)
        {
            case MigrationStatus.InvalidConfig:
                _logger.LogError("Project config is invalid");
                message = new ProjectErrorMessage(ProjectErrorType.InvalidProjectConfig, projectFileName);
                messengerService.Send(message);
                break;

            case MigrationStatus.IncompatibleVersion:
                _logger.LogError("Project version is not compatible with application version");
                message = new ProjectErrorMessage(ProjectErrorType.IncompatibleVersion, projectFileName);
                messengerService.Send(message);
                break;

            case MigrationStatus.InvalidVersion:
                _logger.LogError("Project version is invalid");
                message = new ProjectErrorMessage(ProjectErrorType.InvalidVersion, projectFileName);
                messengerService.Send(message);
                break;

            case MigrationStatus.Failed:
                _logger.LogError("Project migration failed");
                message = new ProjectErrorMessage(ProjectErrorType.MigrationError, projectFileName);
                messengerService.Send(message);
                break;
        }
    }
}
