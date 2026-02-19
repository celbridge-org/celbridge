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
    private readonly IEditorSettings _editorSettings;
    private readonly IWindowModeService _windowModeService;
    private readonly ILayoutService _layoutService;
    private readonly IWorkspaceService _workspaceService;
    private readonly IDialogService _dialogService;
    private readonly IProjectService _projectService;
    private readonly WorkspaceLoader _workspaceLoader;

    public CancellationTokenSource? LoadProjectCancellationToken { get; set; }

    // Panel width/height properties now use Primary/Secondary naming
    public float PrimaryPanelWidth
    {
        get => _editorSettings.PrimaryPanelWidth;
        set => _editorSettings.PrimaryPanelWidth = value;
    }

    public float SecondaryPanelWidth
    {
        get => _editorSettings.SecondaryPanelWidth;
        set => _editorSettings.SecondaryPanelWidth = value;
    }

    public float ConsolePanelHeight
    {
        get => _editorSettings.ConsolePanelHeight;
        set => _editorSettings.ConsolePanelHeight = value;
    }

    public float RestoreConsoleHeight
    {
        get => _editorSettings.RestoreConsoleHeight;
        set => _editorSettings.RestoreConsoleHeight = value;
    }

    public bool IsFullScreen => _windowModeService.IsFullScreen;

    // Panel visibility properties now use Primary/Secondary naming
    public bool IsPrimaryPanelVisible => _layoutService.IsContextPanelVisible;

    public bool IsSecondaryPanelVisible => _layoutService.IsInspectorPanelVisible;

    public bool IsConsolePanelVisible => _layoutService.IsConsolePanelVisible;

    public bool IsConsoleMaximized => _layoutService.IsConsoleMaximized;

    public WorkspacePageViewModel(
        IWorkspaceLogger logger,
        IServiceProvider serviceProvider,
        IMessengerService messengerService,
        IStringLocalizer stringLocalizer,
        IEditorSettings editorSettings,
        IWindowModeService windowModeService,
        ILayoutService layoutService,
        IDialogService dialogService,
        IProjectService projectService,
        WorkspaceLoader workspaceLoader)
    {
        _logger = logger;
        _messengerService = messengerService;
        _stringLocalizer = stringLocalizer;
        _editorSettings = editorSettings;
        _windowModeService = windowModeService;
        _layoutService = layoutService;
        _dialogService = dialogService;
        _projectService = projectService;
        _workspaceLoader = workspaceLoader;

        _editorSettings.PropertyChanged += OnEditorSettings_PropertyChanged;
        
        // Listen for layout manager state changes via messages
        _messengerService.Register<WindowModeChangedMessage>(this, OnWindowModeChanged);
        _messengerService.Register<RegionVisibilityChangedMessage>(this, OnRegionVisibilityChanged);
        _messengerService.Register<ConsoleMaximizedChangedMessage>(this, OnConsoleMaximizedChanged);

        // Create the workspace service and notify the user interface service
        _workspaceService = serviceProvider.GetRequiredService<IWorkspaceService>();
        var message = new WorkspaceServiceCreatedMessage(_workspaceService);
        _messengerService.Send(message);
        _workspaceLoader = workspaceLoader;
    }

    private void OnEditorSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Forward property change notifications from editor settings
        OnPropertyChanged(e);
    }

    private void OnWindowModeChanged(object recipient, WindowModeChangedMessage message)
    {
        // Notify that IsFullScreen might have changed
        OnPropertyChanged(nameof(IsFullScreen));
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

    public void OnWorkspacePageUnloaded()
    {
        _editorSettings.PropertyChanged -= OnEditorSettings_PropertyChanged;

        // Unregister message handlers
        _messengerService.Unregister<WindowModeChangedMessage>(this);
        _messengerService.Unregister<RegionVisibilityChangedMessage>(this);
        _messengerService.Unregister<ConsoleMaximizedChangedMessage>(this);

        // Clear shortcut buttons from the title bar before disposing the workspace
        _workspaceLoader.ClearTitleBarShortcuts();

        // Dispose the workspace service
        // This disposes all the sub-services and releases all resources held by the workspace.
        var disposableWorkspace = _workspaceService as IDisposable;
        Guard.IsNotNull(disposableWorkspace);
        disposableWorkspace.Dispose();

        // Notify listeners that the workspace has been unloaded.
        var message = new WorkspaceUnloadedMessage();
        _messengerService.Send(message);
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

        // Log how long it took to open the workspace
        stopWatch.Stop();
        var elapsed = (long)stopWatch.Elapsed.TotalMilliseconds;
        _logger.LogDebug($"Workspace loaded in {elapsed} ms");

        // Short delay so that the progress bar continues to display while the last document is reopening.
        // If there are no documents to open, this gives the user a chance to visually register the
        // progress bar updating, which feels more responsive than having the progress bar flash on screen momentarily.
        await Task.Delay(1000);

        LoadProjectCancellationToken = null;

        if (loadResult.IsSuccess)
        {
            var message = new WorkspaceLoadedMessage();
            _messengerService.Send(message);
        }
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

