using Celbridge.BaseLibrary.Messaging;
using Celbridge.BaseLibrary.Settings;
using Celbridge.BaseLibrary.UserInterface;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Windows.Input;

namespace Celbridge.Workspace.ViewModels;

public partial class WorkspacePageViewModel : INotifyPropertyChanged, IWorkspace
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IMessengerService _messengerService;
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly IEditorSettings _editorSettings;

    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkspacePageViewModel(IServiceProvider serviceProvider,
        IMessengerService messengerService,
        IUserInterfaceService userInterface,
        IEditorSettings editorSettings)
    {
        _serviceProvider = serviceProvider;
        _messengerService = messengerService;
        _userInterfaceService = userInterface;

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
    /// The view registers with this event to be notified when a workspace panel has been created.
    /// </summary>
    public event Action<WorkspacePanelType, UserControl>? WorkspacePanelAdded;

    public void OnWorkspacePageLoaded()
    {
        // Inform the user interface service that the workspace page has loaded
        var message = new WorkspacePageLoadedMessage(this);
        _messengerService.Send(message);

        foreach (var config in _userInterfaceService.WorkspacePanelConfigs)
        {
            // Instantiate the panel
            var panel = _serviceProvider.GetRequiredService(config.ViewType) as UserControl;
            if (panel is null)
            {
                throw new Exception($"Failed to create a workspace panel of type '{config.ViewType}'");
            }

            // Attach the panel to the workspace UI
            WorkspacePanelAdded?.Invoke(config.PanelType, panel);
        }
    }
}

