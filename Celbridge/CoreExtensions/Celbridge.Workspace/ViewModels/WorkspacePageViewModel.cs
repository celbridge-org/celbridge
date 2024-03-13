using Celbridge.BaseLibrary.Settings;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using System.Windows.Input;

namespace Celbridge.Workspace.ViewModels;

public partial class WorkspacePageViewModel : INotifyPropertyChanged
{
    private readonly IEditorSettings _editorSettings;

    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkspacePageViewModel(IEditorSettings editorSettings)
    {
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
}

