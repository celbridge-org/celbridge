using Celbridge.BaseLibrary.Messaging;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Settings;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Windows.Input;

namespace Celbridge.Workspace.ViewModels;

public partial class WorkspacePageViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly IEditorSettings _editorSettings;
    private readonly IWorkspaceService _workspaceService;
    private readonly IProjectDataService _projectDataService;

    public WorkspacePageViewModel(
        IServiceProvider serviceProvider,
        IMessengerService messengerService,
        IEditorSettings editorSettings,
        IProjectDataService projectDataService)
    {
        _messengerService = messengerService;
        _projectDataService = projectDataService;

        _editorSettings = editorSettings;
        _editorSettings.PropertyChanged += OnSettings_PropertyChanged;

        // Create the workspace service and notify the user interface service
        _workspaceService = serviceProvider.GetRequiredService<IWorkspaceService>();
        var message = new WorkspaceServiceCreatedMessage(_workspaceService);
        _messengerService.Send(message);
    }

    private void OnSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Forward editor setting change notifications to this View Model class
        OnPropertyChanged(e);
    }

    public float LeftPanelWidth
    {
        get => _editorSettings.LeftPanelWidth;
        set => _editorSettings.LeftPanelWidth = value;
    }

    public float RightPanelWidth
    {
        get => _editorSettings.RightPanelWidth;
        set => _editorSettings.RightPanelWidth = value;
    }

    public float BottomPanelHeight
    {
        get => _editorSettings.BottomPanelHeight;
        set => _editorSettings.BottomPanelHeight = value;
    }

    public bool IsLeftPanelVisible
    {
        get => _editorSettings.IsLeftPanelVisible;
        set => _editorSettings.IsLeftPanelVisible = value;
    }

    public bool IsRightPanelVisible
    {
        get => _editorSettings.IsRightPanelVisible;
        set => _editorSettings.IsRightPanelVisible = value;
    }

    public bool IsBottomPanelVisible
    {
        get => _editorSettings.IsBottomPanelVisible;
        set => _editorSettings.IsBottomPanelVisible = value;
    }

    public ICommand ToggleLeftPanelCommand => new RelayCommand(ToggleLeftPanel_Executed);
    private void ToggleLeftPanel_Executed()
    {
        _editorSettings.IsLeftPanelVisible = !_editorSettings.IsLeftPanelVisible;
    }

    public ICommand ToggleRightPanelCommand => new RelayCommand(ToggleRightPanel_Executed);
    private void ToggleRightPanel_Executed()
    {
        _editorSettings.IsRightPanelVisible = !_editorSettings.IsRightPanelVisible;
    }

    public ICommand ToggleBottomPanelCommand => new RelayCommand(ToggleBottomPanel_Executed);
    private void ToggleBottomPanel_Executed()
    {
        _editorSettings.IsBottomPanelVisible = !_editorSettings.IsBottomPanelVisible;
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

    public async Task<Result> LoadWorkspaceAsync()
    {
        //
        // Acquire the workspace database
        //
        var workspaceDataService = _workspaceService.WorkspaceDataService;
        var acquireResult = await workspaceDataService.AcquireWorkspaceDataAsync();
        if (acquireResult.IsFailure)
        {
            return Result.Fail($"Failed to load workspace. {acquireResult.Error}");
        }

        var workspaceData = workspaceDataService.LoadedWorkspaceData;
        Guard.IsNotNull(workspaceData);

        //
        // Restore the Project Panel view state
        //
        try
        {
            // Set expanded folders
            var getFoldersResult = await workspaceData.GetExpandedFoldersAsync();
            if (getFoldersResult.IsSuccess)
            {
                var expandedFolders = getFoldersResult.Value;
                var resourceRegistry = _workspaceService.ProjectService.ResourceRegistry;

                foreach (var expandedFolder in expandedFolders)
                {
                    resourceRegistry.SetFolderIsExpanded(expandedFolder, true);
                }
            }
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to restore Project Panel view state. {ex.Message}");
        }

        //
        // Update the resource registry.
        //
        try
        {
            var resourceRegistry = _workspaceService.ProjectService.ResourceRegistry;
            var updateResult = resourceRegistry.UpdateResourceTree();
            if (updateResult.IsFailure)
            {
                return Result.Fail($"Failed to load workspace. {updateResult.Error}");
            }
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to update the resource registry. {ex.Message}");
        }

        // Todo: Load the workspace here
        await Task.Delay(500);

        var message = new WorkspaceLoadedMessage();
        _messengerService.Send(message);

        return Result.Ok();
    }
}

