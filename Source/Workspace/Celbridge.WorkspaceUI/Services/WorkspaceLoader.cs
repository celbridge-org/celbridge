using System.Text;
using Celbridge.Commands;
using Celbridge.Console;
using Celbridge.Documents;
using Celbridge.Logging;
using Celbridge.Packages;
using Celbridge.Platform;
using Celbridge.Projects;
using Celbridge.Server;
using Celbridge.Settings;
using Celbridge.UserInterface;

namespace Celbridge.WorkspaceUI.Services;

public class WorkspaceLoader
{
    private readonly ILogger<WorkspaceLoader> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly IFeatureFlags _featureFlags;
    private readonly IProjectService _projectService;
    private readonly IServerService _serverService;
    private readonly ProjectCheckReporter _projectCheckReporter;
    private readonly IProjectLoadReporter _loadReporter;
    private readonly IAppEnvironment _appEnvironment;

    public WorkspaceLoader(
        ILogger<WorkspaceLoader> logger,
        IWorkspaceWrapper workspaceWrapper,
        IUserInterfaceService userInterfaceService,
        IFeatureFlags featureFlags,
        IProjectService projectService,
        IServerService serverService,
        ProjectCheckReporter projectCheckReporter,
        IProjectLoadReporter loadReporter,
        IAppEnvironment appEnvironment)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
        _userInterfaceService = userInterfaceService;
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
            // empty ignore set, so it does not currently fail. This branch is the
            // intended handling once [resources] config validation can fail: warn
            // and continue rather than fail project load, because the *.celbridge
            // config stays reachable (system-allow) for the user to correct.
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

        // Populate title bar shortcut buttons from project config.
        PopulateTitleBarShortcuts();

        // Notify that the workspace has loaded.
        var messengerService = ServiceLocator.AcquireService<IMessengerService>();
        var workspaceLoadedMessage = new WorkspaceLoadedMessage();
        messengerService.Send(workspaceLoadedMessage);

        // Initialize terminal window and Python scripting. These run after the workspace is considered
        // "loaded" because they don't block the workspace UI from being functional.

        // Only initialize console and Python if the console-panel feature is enabled.
        var isConsolePanelEnabled = _featureFlags.IsEnabled(FeatureFlagConstants.ConsolePanel);
        if (isConsolePanelEnabled)
        {
            var consoleService = workspaceService.ConsoleService;
            var initTerminal = await consoleService.InitializeTerminalWindow();
            if (initTerminal.IsFailure)
            {
                // Workspace loading continues even if terminal initialization fails
                _logger.LogError(initTerminal.FirstException, "Failed to initialize console terminal: {Error}", initTerminal.DiagnosticReport);
            }

            // Initialize Python scripting
            // If Python fails to initialize, the error is reported and the project continues to load.
            await TryInitializePythonAsync(workspaceService);
        }
        else
        {
            _logger.LogInformation("Console panel is disabled by feature flag");
        }

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

        var shortcutsSection = currentProject.Config.Shortcuts;
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

    private void PopulateTitleBarShortcuts()
    {
        // Only populate shortcuts if console panel is enabled
        var isConsolePanelEnabled = _featureFlags.IsEnabled(FeatureFlagConstants.ConsolePanel);
        if (!isConsolePanelEnabled)
        {
            return;
        }

        var projectService = ServiceLocator.AcquireService<IProjectService>();
        var currentProject = projectService.CurrentProject;
        if (currentProject is null)
        {
            return;
        }

        var shortcutsSection = currentProject.Config.Shortcuts;
        if (shortcutsSection.HasErrors)
        {
            // Error notification is handled by TryInitializePythonAsync
            return;
        }

        var titleBar = _userInterfaceService.TitleBar;
        if (titleBar is null)
        {
            return;
        }

        var shortcuts = shortcutsSection.Definitions
            .Select(d => new Shortcut
            {
                Name = d.Name,
                Icon = d.Icon,
                Tooltip = d.Tooltip,
                Script = d.Script
            })
            .ToList();

        titleBar.BuildShortcutButtons(shortcuts, (script) =>
        {
            _workspaceWrapper.WorkspaceService.ConsoleService.RunCommand(script);
        });
    }

    public void ClearTitleBarShortcuts()
    {
        _userInterfaceService.TitleBar?.ClearShortcutButtons();
    }

    // Creates the persistent surface for every utility (bundled before project, each by id) and builds the rail.
    // The utilities are owned by the documents service for the workspace lifetime. The utility mechanism is
    // always on; individual utility packages can still gate themselves with a package feature flag.
    private async Task BuildUtilities()
    {
        var utilityContributions = GetUtilityContributions();
        if (utilityContributions.Count == 0)
        {
            return;
        }

        var utilityService = _workspaceWrapper.WorkspaceService.UtilityService;
        var tabs = await utilityService.CreateUtilitiesAsync(utilityContributions);
        if (tabs.Count == 0)
        {
            return;
        }

        _workspaceWrapper.WorkspaceService.UtilityPanel.BuildContributedUtilities(tabs);
    }

    // Enumerates enabled utility contributions in the rail's stable order: bundled before project, each
    // group sorted by fully-qualified id. Feature-flag-disabled packages are filtered out.
    private List<CustomDocumentEditorContribution> GetUtilityContributions()
    {
        var packageService = _workspaceWrapper.WorkspaceService.PackageService;

        var utilityContributions = new List<CustomDocumentEditorContribution>();
        foreach (var contribution in packageService.GetAllDocumentEditors())
        {
            if (contribution is not CustomDocumentEditorContribution { IsUtility: true } utilityContribution)
            {
                continue;
            }

            if (!IsPackageEnabled(utilityContribution.Package))
            {
                continue;
            }

            utilityContributions.Add(utilityContribution);
        }

        var bundledContributions = utilityContributions
            .Where(contribution => contribution.Package.Origin == PackageOrigin.Bundled)
            .OrderBy(GetUtilityId, StringComparer.Ordinal);

        var projectContributions = utilityContributions
            .Where(contribution => contribution.Package.Origin == PackageOrigin.Project)
            .OrderBy(GetUtilityId, StringComparer.Ordinal);

        return bundledContributions.Concat(projectContributions).ToList();
    }

    // The utility id string, used only as a stable ordinal sort key for the rail ordering.
    private static string GetUtilityId(CustomDocumentEditorContribution contribution)
    {
        return UtilityId.Create(contribution.Package.Name, contribution.Id).ToString();
    }

    private bool IsPackageEnabled(PackageInfo package)
    {
        if (string.IsNullOrEmpty(package.FeatureFlag))
        {
            return true;
        }

        return _featureFlags.IsEnabled(package.FeatureFlag);
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
