using Celbridge.Dialog;
using Celbridge.Projects;
using Celbridge.Settings;
using Celbridge.UserInterface;
using Celbridge.WorkspaceUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;
using System.ComponentModel;
using System.Diagnostics;

namespace Celbridge.WorkspaceUI.ViewModels;

using IWorkspaceLogger = Logging.ILogger<WorkspaceViewModel>;

public partial class WorkspaceViewModel : ObservableObject
{
    private readonly IWorkspaceLogger _logger;
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IWindowModeService _windowModeService;
    private readonly ILayoutService _layoutService;
    private readonly IFeatureFlags _featureFlags;
    private readonly IWorkspaceService _workspaceService;
    private readonly IDialogService _dialogService;
    private readonly IProjectService _projectService;
    private readonly WorkspaceLoader _workspaceLoader;

    public CancellationTokenSource? LoadCancellation { get; set; }

    public WorkspaceViewModel(
        IWorkspaceLogger logger,
        IServiceProvider serviceProvider,
        IMessengerService messengerService,
        IStringLocalizer stringLocalizer,
        IWindowModeService windowModeService,
        ILayoutService layoutService,
        IFeatureFlags featureFlags,
        IDialogService dialogService,
        IProjectService projectService,
        WorkspaceLoader workspaceLoader)
    {
        _logger = logger;
        _messengerService = messengerService;
        _stringLocalizer = stringLocalizer;
        _windowModeService = windowModeService;
        _layoutService = layoutService;
        _featureFlags = featureFlags;
        _dialogService = dialogService;
        _projectService = projectService;
        _workspaceLoader = workspaceLoader;

        // Create the workspace service and notify the user interface service
        _workspaceService = serviceProvider.GetRequiredService<IWorkspaceService>();
        var message = new WorkspaceServiceCreatedMessage(_workspaceService);
        _messengerService.Send(message);
    }

    public async Task OnWorkspaceViewUnloadedAsync()
    {
        // Best-effort: persist editor state while the editors are still alive, then close the panels. A
        // failure here (e.g. the project folder was deleted while the project was open) must not prevent the
        // dispose and unload notification below, which is a separate step so it still runs.
        try
        {
            // Save editor states before closing documents, while editors are still alive.
            await _workspaceService.DocumentsService.StoreDocumentEditorStates();

            // Close all open documents and clean up their WebView2 resources.
            _workspaceService.DocumentsPanel.Shutdown();

            // Tear down the utilities, then clear the rail.
            await _workspaceService.UtilityService.TeardownUtilitiesAsync();
            _workspaceService.UtilityPanel.ClearRailItems();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to save workspace state during teardown");
        }

        // Tear down and dispose the workspace. Guarded so the unload notification is still sent on failure.
        try
        {
            // Clear project-level feature flag overrides before disposing the workspace
            _featureFlags.ClearProjectOverrides();

            // Revert the process working folder set on load, so it stays valid while no project is loaded
            _workspaceLoader.ResetProcessWorkingFolder();

            // Dispose the workspace service
            var disposableWorkspace = _workspaceService as IDisposable;
            Guard.IsNotNull(disposableWorkspace);
            disposableWorkspace.Dispose();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Workspace teardown failed");
        }

        // Notify listeners that the workspace has been unloaded. This must always be sent, even after a
        // failure above, because the project-unload wait completes only when this message clears the
        // workspace loaded state.
        var message = new WorkspaceUnloadedMessage();
        _messengerService.Send(message);
    }

    public async Task<Result> AcquireWorkspaceSettingsAsync()
    {
        return await _workspaceService.WorkspaceSettings.AcquireWorkspaceSettingsAsync();
    }

    public async Task LoadWorkspaceAsync()
    {
        // Show the progress dialog with the project name
        var projectName = _projectService.CurrentProject?.ProjectName ?? string.Empty;
        var loadingProjectString = _stringLocalizer.GetString("Workspace_LoadingProject", projectName);
        using var progressDialogToken = _dialogService.AcquireProgressDialog(loadingProjectString);

        // Time how long it takes to open the workspace
        var stopWatch = new Stopwatch();
        stopWatch.Start();

        // Load and initialize the workspace using the helper class
        var loadResult = await _workspaceLoader.LoadWorkspaceAsync();
        if (loadResult.IsFailure)
        {
            _logger.LogError(loadResult, "Failed to load workspace");

            // Notify the waiting LoadProject async method that a failure has occured via the cancellation token.
            if (LoadCancellation is not null)
            {
                LoadCancellation.Cancel();
            }
        }

        LoadCancellation = null;

        // Log how long it took to open the workspace
        stopWatch.Stop();
        var elapsed = (long)stopWatch.Elapsed.TotalMilliseconds;
        _logger.LogDebug($"Workspace loaded in {elapsed} ms");

    }
}

