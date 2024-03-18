namespace Celbridge.Legacy.ViewModels;

public partial class RightNavigationBarViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;

    public RightNavigationBarViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public XamlRoot? ShellRoot { get; set; }

    public ICommand ToggleInspectorCommand => new RelayCommand(ToggleInspector_Executed);

    private void ToggleInspector_Executed()
    {
        // Toggle the inspector panel
        Guard.IsNotNull(_settingsService.EditorSettings);
        _settingsService.EditorSettings.IsRightPanelVisible = !_settingsService.EditorSettings.IsRightPanelVisible;
    }
}
