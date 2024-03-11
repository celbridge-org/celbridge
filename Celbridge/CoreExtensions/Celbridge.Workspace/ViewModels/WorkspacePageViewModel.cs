using Celbridge.BaseLibrary.Settings;

namespace Celbridge.Workspace.ViewModels;

public partial class WorkspacePageViewModel : INotifyPropertyChanged
{
    private readonly IEditorSettings _settings;

    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkspacePageViewModel(IEditorSettings editorSettings)
    {
        _settings = editorSettings;
        _settings.PropertyChanged += OnSettings_PropertyChanged;
    }

    private void OnSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Forward the change notification to the view
        PropertyChanged?.Invoke(this, e);
    }

    public void OnView_Unloaded()
    {
        _settings.PropertyChanged -= OnSettings_PropertyChanged;
    }

    public float LeftPanelWidth
    {
        get => _settings.LeftPanelWidth;
        set => _settings.LeftPanelWidth = value;
    }

    public float RightPanelWidth
    {
        get => _settings.RightPanelWidth;
        set => _settings.RightPanelWidth = value;
    }

    public float BottomPanelHeight
    {
        get => _settings.BottomPanelHeight;
        set => _settings.BottomPanelHeight = value;
    }

    public bool LeftPanelExpanded
    {
        get => _settings.LeftPanelExpanded;
        set => _settings.LeftPanelExpanded = value;
    }

    public bool RightPanelExpanded
    {
        get => _settings.RightPanelExpanded;
        set => _settings.RightPanelExpanded = value;
    }

    public bool BottomPanelExpanded
    {
        get => _settings.BottomPanelExpanded;
        set => _settings.BottomPanelExpanded = value;
    }
}

