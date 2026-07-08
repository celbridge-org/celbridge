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

using IWorkspaceLogger = Logging.ILogger<WorkspacePageViewModel>;

public partial class WorkspacePageViewModel : ObservableObject
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

    public CancellationTokenSource? LoadProjectCancellationToken { get; set; }

    public float PrimaryPanelWidth
    {
        get => _workspaceService.BindableWorkspaceSettings.PrimaryPanelWidth;
        set => _workspaceService.BindableWorkspaceSettings.PrimaryPanelWidth = value;
    }

    public float SecondaryPanelWidth
    {
        get => _workspaceService.BindableWorkspaceSettings.SecondaryPanelWidth;
        set => _workspaceService.BindableWorkspaceSettings.SecondaryPanelWidth = value;
    }

    public float ConsolePanelHeight
    {
        get => _workspaceService.BindableWorkspaceSettings.ConsolePanelHeight;
        set => _workspaceService.BindableWorkspaceSettings.ConsolePanelHeight = value;
    }

    public bool IsPrimaryPanelVisible => _layoutService.IsContextPanelVisible;

    public bool IsSecondaryPanelVisible => _layoutService.IsInspectorPanelVisible;

    public bool IsConsolePanelVisible => _layoutService.IsConsolePanelVisible;

    public bool IsConsoleMaximized => _layoutService.IsConsoleMaximized;

    public WorkspacePageViewModel(
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

        // Listen for layout manager state changes via messages
        _messengerService.Register<RegionVisibilityChangedMessage>(this, OnRegionVisibilityChanged);
        _messengerService.Register<ConsoleMaximizedChangedMessage>(this, OnConsoleMaximizedChanged);

        // Create the workspace service and notify the user interface service
        _workspaceService = serviceProvider.GetRequiredService<IWorkspaceService>();
        var message = new WorkspaceServiceCreatedMessage(_workspaceService);
        _messengerService.Send(message);
        _workspaceLoader = workspaceLoader;

        // Forward panel-size change notifications from the workspace settings so the
        // bound panel columns update when the layout is reset or restored.
        _workspaceService.BindableWorkspaceSettings.PropertyChanged += OnWorkspaceSettings_PropertyChanged;
    }

    private void OnWorkspaceSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Forward property change notifications from the workspace settings facade
        OnPropertyChanged(e);
    }

    private void OnRegionVisibilityChanged(object recipient, RegionVisibilityChangedMessage message)
    {
        // Notify that panel visibility properties have changed
        OnPropertyChanged(nameof(IsPrimaryPanelVisible));
        OnPropertyChanged(nameof(IsSecondaryPanelVisible));
        OnPropertyChanged(nameof(IsConsolePanelVisible));
    }

    private void OnConsoleMaximizedChanged(object recipient, ConsoleMaximizedChangedMessage message)
    {
        // Notify that console maximized state has changed
        OnPropertyChanged(nameof(IsConsoleMaximized));
    }

    public async Task OnWorkspacePageUnloadedAsync()
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

            _workspaceService.ConsolePanel?.Shutdown();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to save workspace state during page unload");
        }

        // Tear down and dispose the workspace. Guarded so the unload notification is still sent on failure.
        try
        {
            _workspaceService.BindableWorkspaceSettings.PropertyChanged -= OnWorkspaceSettings_PropertyChanged;

            // Unregister message handlers
            _messengerService.Unregister<RegionVisibilityChangedMessage>(this);
            _messengerService.Unregister<ConsoleMaximizedChangedMessage>(this);

            // Clear project-level feature flag overrides before disposing the workspace
            _featureFlags.ClearProjectOverrides();

            // Clear shortcut buttons from the title bar before disposing the workspace
            _workspaceLoader.ClearTitleBarShortcuts();

            // Revert the process working folder set on load, so it stays valid while no project is loaded
            _workspaceLoader.ResetProcessWorkingFolder();

            // Dispose the workspace service
            // This disposes all the sub-services and releases all resources held by the workspace.
            var disposableWorkspace = _workspaceService as IDisposable;
            Guard.IsNotNull(disposableWorkspace);
            disposableWorkspace.Dispose();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Workspace teardown failed during page unload");
        }

        // Notify listeners that the workspace has been unloaded. This must always be sent, even after a
        // failure above, because the ProjectUnloader wait completes only when this message clears the
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
        var loadingProjectString = _stringLocalizer.GetString("WorkspacePage_LoadingProject", projectName);
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
            if (LoadProjectCancellationToken is not null)
            {
                LoadProjectCancellationToken.Cancel();
            }
        }

        LoadProjectCancellationToken = null;

        // Log how long it took to open the workspace
        stopWatch.Stop();
        var elapsed = (long)stopWatch.Elapsed.TotalMilliseconds;
        _logger.LogDebug($"Workspace loaded in {elapsed} ms");

    }

    public void SetActivePanel(WorkspacePanel panel)
    {
        if (_workspaceService.ActivePanel != panel)
        {
            // Setter is not exposed in public API
            (_workspaceService as WorkspaceService)!.ActivePanel = panel;
        }
    }
}

