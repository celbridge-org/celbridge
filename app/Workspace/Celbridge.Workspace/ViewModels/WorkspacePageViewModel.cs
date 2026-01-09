using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Messaging;
using Celbridge.Settings;
using Celbridge.Workspace.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using System.ComponentModel;
using System.Diagnostics;

namespace Celbridge.Workspace.ViewModels;

using IWorkspaceLogger = Logging.ILogger<WorkspacePageViewModel>;

public partial class WorkspacePageViewModel : ObservableObject
{
    private readonly IWorkspaceLogger _logger;
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IEditorSettings _editorSettings;
    private readonly IWorkspaceService _workspaceService;
    private readonly IDialogService _dialogService;
    private readonly WorkspaceLoader _workspaceLoader;
    
    private IProgressDialogToken? _progressDialogToken;

    public CancellationTokenSource? LoadProjectCancellationToken { get; set; }

    public WorkspacePageViewModel(
        IWorkspaceLogger logger,
        IServiceProvider serviceProvider,
        IMessengerService messengerService,
        IStringLocalizer stringLocalizer,
        ICommandService commandService,
        IEditorSettings editorSettings,
        IDialogService dialogService,
        WorkspaceLoader workspaceLoader)
    {
        _logger = logger;
        _messengerService = messengerService;
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
        _editorSettings = editorSettings;
        _dialogService = dialogService;
        _workspaceLoader = workspaceLoader;

        _editorSettings.PropertyChanged += OnSettings_PropertyChanged;

        // Create the workspace service and notify the user interface service
        _workspaceService = serviceProvider.GetRequiredService<IWorkspaceService>();
        var message = new WorkspaceServiceCreatedMessage(_workspaceService);
        _messengerService.Send(message);
        _workspaceLoader = workspaceLoader;
    }

    private void OnSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Forward editor setting change notifications to this View Model class
        OnPropertyChanged(e);
    }

    public float ContextPanelWidth
    {
        get => _editorSettings.ContextPanelWidth;
        set => _editorSettings.ContextPanelWidth = value;
    }

    public float InspectorPanelWidth
    {
        get => _editorSettings.InspectorPanelWidth;
        set => _editorSettings.InspectorPanelWidth = value;
    }

    public float ConsolePanelHeight
    {
        get => _editorSettings.ConsolePanelHeight;
        set => _editorSettings.ConsolePanelHeight = value;
    }

    public bool IsContextPanelVisible
    {
        get => _editorSettings.IsContextPanelVisible;
        set => _editorSettings.IsContextPanelVisible = value;
    }

    public bool IsInspectorPanelVisible
    {
        get => _editorSettings.IsInspectorPanelVisible;
        set => _editorSettings.IsInspectorPanelVisible = value;
    }

    public bool IsConsolePanelVisible
    {
        get => _editorSettings.IsConsolePanelVisible;
        set => _editorSettings.IsConsolePanelVisible = value;
    }

    public void OnWorkspacePageUnloaded()
    {
        _editorSettings.PropertyChanged -= OnSettings_PropertyChanged;

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
        // Show the progress dialog
        var loadingWorkspaceString = _stringLocalizer.GetString("WorkspacePage_LoadingWorkspace");
        _progressDialogToken = _dialogService.AcquireProgressDialog(loadingWorkspaceString);

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

        // Hide the progress dialog
        _dialogService.ReleaseProgressDialog(_progressDialogToken);

        _progressDialogToken = null;
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

