using Celbridge.BaseLibrary.Messaging;
using Celbridge.BaseLibrary.Settings;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.Workspace.Services;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Windows.Input;

namespace Celbridge.Workspace.ViewModels;

public partial class WorkspacePageViewModel : INotifyPropertyChanged
{
    private readonly IMessengerService _messengerService;
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly IEditorSettings _editorSettings;
    private readonly IWorkspaceService _workspaceService;

    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkspacePageViewModel(IMessengerService messengerService,
        IUserInterfaceService userInterface,
        IEditorSettings editorSettings,
        IWorkspaceService workspaceService)
    {
        _messengerService = messengerService;
        _userInterfaceService = userInterface;
        _workspaceService = workspaceService; // Transient instance created at the same time as the view model

        _editorSettings = editorSettings;
        _editorSettings.PropertyChanged += OnSettings_PropertyChanged;
    }

    private void OnSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Forward the change notification to the view
        PropertyChanged?.Invoke(this, e);
    }

    public void OnView_Unloaded()
    {
        _editorSettings.PropertyChanged -= OnSettings_PropertyChanged;
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
    /// The WorkspacePage registers with this event to be notified when a workspace panel has been created.
    /// </summary>
    public event Action<Dictionary<WorkspacePanelType, UIElement>>? WorkspacePanelsCreated;

    public void OnWorkspacePageLoaded()
    {
        // Use the concrete type to avoid exposing CreateWorkspacePanels() in the public API
        var workspaceService = _workspaceService as WorkspaceService;
        Guard.IsNotNull(workspaceService);

        var panels = workspaceService.CreateWorkspacePanels();
        WorkspacePanelsCreated?.Invoke(panels);

        // Inform the user interface service that the workspace page has loaded
        var message = new WorkspaceLoadedMessage(_workspaceService);
        _messengerService.Send(message);
    }

    public void OnWorkspacePageUnloaded()
    {
        // Inform the user interface service that the workspace page has been unloaded
        var message = new WorkspaceUnloadedMessage();
        _messengerService.Send(message);
    }
}

