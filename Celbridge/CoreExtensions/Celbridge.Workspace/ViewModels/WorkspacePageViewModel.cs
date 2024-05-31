using Celbridge.BaseLibrary.Messaging;
using Celbridge.BaseLibrary.Settings;
using Celbridge.BaseLibrary.Workspace;
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

    public WorkspacePageViewModel(
        IServiceProvider serviceProvider,
        IMessengerService messengerService,
        IEditorSettings editorSettings)
    {
        _messengerService = messengerService;

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

        // Notify listeners that the workspace service has been destroyed.
        // All workspace related resources must be released at this point.
        var message = new WorkspaceServiceDestroyedMessage();
        _messengerService.Send(message);
    }

    public async Task InitializeWorkspaceAsync()
    {
        // Todo: Setup the workspace here
        await Task.Delay(1000);

        var message = new WorkspaceInitializedMessage();
        _messengerService.Send(message);
    }
}

