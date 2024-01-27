namespace Celbridge.Legacy.ViewModels;

public partial class RightNavigationBarViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IStringLocalizer _localizer;

    public RightNavigationBarViewModel(ISettingsService settingsService, IStringLocalizer localizer)
    {
        _settingsService = settingsService;
        _localizer = localizer;
    }

    public XamlRoot? ShellRoot { get; set; }

    public ICommand ToggleInspectorCommand => new RelayCommand(ToggleInspector_Executed);

    private void ToggleInspector_Executed()
    {
        // Toggle the inspector panel
        Guard.IsNotNull(_settingsService.EditorSettings);
        _settingsService.EditorSettings.RightPanelExpanded = !_settingsService.EditorSettings.RightPanelExpanded;
    }
}
