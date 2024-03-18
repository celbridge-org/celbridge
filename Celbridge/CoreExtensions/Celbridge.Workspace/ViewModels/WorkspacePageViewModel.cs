using Celbridge.BaseLibrary.Messaging;
using Celbridge.BaseLibrary.Settings;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.Workspace.Services;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using System.Windows.Input;

namespace Celbridge.Workspace.ViewModels;

public partial class WorkspacePageViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly IEditorSettings _editorSettings;
    private readonly IWorkspaceService _workspaceService;

    public WorkspacePageViewModel(
        IMessengerService messengerService,
        IEditorSettings editorSettings,
        IWorkspaceService workspaceService)
    {
        _messengerService = messengerService;
        _workspaceService = workspaceService; // Transient instance created by DI

        _editorSettings = editorSettings;
        _editorSettings.PropertyChanged += OnSettings_PropertyChanged;

        PropertyChanged += OnViewModel_PropertyChanged;
    }

    private void OnSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Forward editor setting change notifications to this View Model class
        OnPropertyChanged(e);
    }

    private void OnViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LeftPanelVisible):
            case nameof(RightPanelVisible):
            case nameof(BottomPanelVisible):

                // Notify listeners that the visibility state of the workspace panels has changed
                var message = new WorkspacePanelVisibilityChangedMessage(
                    IsLeftPanelVisible: this.LeftPanelVisible,
                    IsRightPanelVisible: this.RightPanelVisible,
                    IsBottomPanelVisible: this.BottomPanelVisible);
                _messengerService.Send(message);

                break;
        }
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

    public bool LeftPanelVisible
    {
        get => _editorSettings.LeftPanelVisible;
        set => _editorSettings.LeftPanelVisible = value;
    }

    public bool RightPanelVisible
    {
        get => _editorSettings.RightPanelVisible;
        set => _editorSettings.RightPanelVisible = value;
    }

    public bool BottomPanelVisible
    {
        get => _editorSettings.BottomPanelVisible;
        set => _editorSettings.BottomPanelVisible = value;
    }

    public ICommand ToggleLeftPanelCommand => new RelayCommand(ToggleLeftPanel_Executed);
    private void ToggleLeftPanel_Executed()
    {
        _editorSettings.LeftPanelVisible = !_editorSettings.LeftPanelVisible;
    }

    public ICommand ToggleRightPanelCommand => new RelayCommand(ToggleRightPanel_Executed);
    private void ToggleRightPanel_Executed()
    {
        _editorSettings.RightPanelVisible = !_editorSettings.RightPanelVisible;
    }

    public ICommand ToggleBottomPanelCommand => new RelayCommand(ToggleBottomPanel_Executed);
    private void ToggleBottomPanel_Executed()
    {
        _editorSettings.BottomPanelVisible = !_editorSettings.BottomPanelVisible;
    }

    /// <summary>
    /// The WorkspacePage registers with this event to be notified when the workspace panels have been created.
    /// </summary>
    public event Action<Dictionary<WorkspacePanelType, UIElement>>? WorkspacePanelsCreated;

    public void OnWorkspacePageLoaded()
    {
        // Use the concrete type to avoid exposing CreateWorkspacePanels() in the public API
        var workspaceService = _workspaceService as WorkspaceService;
        Guard.IsNotNull(workspaceService);

        // Inform the user interface service that the workspace page has loaded.
        // At this point, the workspace does not yet contain any workspace panels.
        var message = new WorkspaceLoadedMessage(_workspaceService);
        _messengerService.Send(message);

        // Create the previously registered workspace panels.
        // As each WorkspacePanel is instantiated, it creates its own service and registers it with
        // the WorkspaceService.
        var panels = workspaceService.CreateWorkspacePanels();
        WorkspacePanelsCreated?.Invoke(panels);

        // Send a "fake" panel visibility change message so that the workspace panels can configure
        // themselves based on the initial panel visibility state.
        OnPropertyChanged(nameof(LeftPanelVisible));
    }

    public void OnWorkspacePageUnloaded()
    {
        _editorSettings.PropertyChanged -= OnSettings_PropertyChanged;
        PropertyChanged -= OnViewModel_PropertyChanged;

        // Notify listeners that the workspace page has been unloaded.
        // All workspace related resources must be released at this point.
        var message = new WorkspaceUnloadedMessage();
        _messengerService.Send(message);
    }
}

