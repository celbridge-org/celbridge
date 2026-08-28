using System.Text;
using Celbridge.Workshop;
using Celbridge.Logging;
using Celbridge.Packages;
using Celbridge.Platform;
using Celbridge.Projects;
using Celbridge.Reports;
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
    private readonly IProjectLoadReporter _loadReporter;
    private readonly IProjectHealthService _projectHealthService;
    private readonly IAppEnvironment _appEnvironment;
    private readonly IWorkshopService _workshopService;

    public WorkspaceLoader(
        ILogger<WorkspaceLoader> logger,
        IWorkspaceWrapper workspaceWrapper,
        IFeatureFlags featureFlags,
        IProjectService projectService,
        IServerService serverService,
        IProjectLoadReporter loadReporter,
        IProjectHealthService projectHealthService,
        IAppEnvironment appEnvironment,
        IWorkshopService workshopService)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
        _featureFlags = featureFlags;
        _projectService = projectService;
        _serverService = serverService;
        _loadReporter = loadReporter;
        _projectHealthService = projectHealthService;
        _appEnvironment = appEnvironment;
        _workshopService = workshopService;
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

            // Record entries the config parser skipped or degraded. The rest of the file applied, and
            // the load report is where the detail lands.
            HandleConfigEntryErrors(currentProject.Config.EntryErrors, currentProject.ProjectFilePath);
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

            // Write the Workshop document before the first resource scan. The temp: root that holds it is
            // wiped on every load, and an open document checks the disk whenever the registry updates, so a
            // Workshop tab left open last session has to find its file already back in place.
            await _workshopService.SeedDocumentAsync();

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

        // Written last, so the report covers everything the load recorded along the way.
        await WriteLoadReportAsync();

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

    // Errors are logged, never thrown — a report that could not be written must not fail workspace load.
    private async Task WriteLoadReportAsync()
    {
        try
        {
            var registry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

            // The sidecar snapshot is a by-product of the registry build that has already run, so this is
            // a read rather than a check.
            _loadReporter.RecordSidecarReport(registry.GetSidecarReport());
            RecordResourceCounts();

            var reportSummary = await _loadReporter.FlushAsync();
            if (reportSummary is null)
            {
                return;
            }

            // Recorded in every state, including a clean load: the switcher's health row states what the
            // load found whether or not it found anything, and needs a report to open either way.
            _projectHealthService.SetHealth(reportSummary);

            if (reportSummary.Severity == ReportSeverity.Info)
            {
                return;
            }

            // One notification for the whole load: everything it covers is in the report the action opens.
            // Raised after the flush, so that report is already on disk.
            var messengerService = ServiceLocator.AcquireService<IMessengerService>();
            var message = new ProjectLoadNotificationMessage(reportSummary);
            messengerService.Send(message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write the project load report.");
        }
    }

    private void RecordResourceCounts()
    {
        var workspaceService = _workspaceWrapper.WorkspaceService;
        if (workspaceService is null)
        {
            return;
        }

        var projectFolder = workspaceService.ResourceService.Registry.ProjectFolder;

        var fileCount = 0;
        var folderCount = 0;
        CountResources(projectFolder, ref fileCount, ref folderCount);

        _loadReporter.RecordResourceCounts(fileCount, folderCount);
    }

    private static void CountResources(IFolderResource folder, ref int fileCount, ref int folderCount)
    {
        foreach (var child in folder.Children)
        {
            if (child is IFolderResource childFolder)
            {
                folderCount++;
                CountResources(childFolder, ref fileCount, ref folderCount);
            }
            else
            {
                fileCount++;
            }
        }
    }

    // Assembles the rail register and has the panel render it. The utilities are owned by the utility service
    // for the workspace lifetime; the panel's own built-in items join the register first so the register
    // holds the rail in the order the panel draws it.
    private async Task BuildUtilities()
    {
        var utilityPanel = _workspaceWrapper.WorkspaceService.UtilityPanel;
        var utilityService = _workspaceWrapper.WorkspaceService.UtilityService;

        utilityService.RegisterBuiltInUtilityItems(utilityPanel.GetBuiltInUtilityItems());

        await utilityService.CreateUtilitiesAsync(GetUtilityInstances());

        utilityPanel.BuildRailItems(utilityService.GetRailItems());
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

        _loadReporter.RecordConfigEntryErrors(entryErrors);

        var projectFileName = Path.GetFileName(projectFilePath);

        var sb = new StringBuilder();
        sb.AppendLine($"Project config entries in '{projectFileName}' were skipped or degraded:");
        foreach (var entryError in entryErrors)
        {
            sb.AppendLine($"  [{entryError.EntryName}]: {entryError.Message}");
        }
        _logger.LogError(sb.ToString());
    }
}
