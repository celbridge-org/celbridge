namespace Celbridge.Legacy.ViewModels;

public partial class LeftNavigationBarViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;

    public LeftNavigationBarViewModel(ISettingsService settingsService, IDialogService dialogService)
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
    }

    public ICommand ToggleLeftPanelCommand => new RelayCommand(ToggleLeftPanel_Executed);
    private void ToggleLeftPanel_Executed()
    {
        Guard.IsNotNull(_settingsService.EditorSettings);
        _settingsService.EditorSettings.LeftPanelVisible = !_settingsService.EditorSettings.LeftPanelVisible;
    }

    public ICommand ToggleRightPanelCommand => new RelayCommand(ToggleRightPanel_Executed);
    private void ToggleRightPanel_Executed()
    {
        Guard.IsNotNull(_settingsService.EditorSettings);
        _settingsService.EditorSettings.RightPanelVisible = !_settingsService.EditorSettings.RightPanelVisible;
    }

    public ICommand ToggleBottomPanelCommand => new RelayCommand(ToggleBottomPanel_Executed);
    private void ToggleBottomPanel_Executed()
    {
        Guard.IsNotNull(_settingsService.EditorSettings);
        _settingsService.EditorSettings.BottomPanelVisible = !_settingsService.EditorSettings.BottomPanelVisible;
    }

    public ICommand ToggleAllPanelsCommand => new RelayCommand(ToggleAllPanels_Executed);
    private void ToggleAllPanels_Executed()
    {
        Guard.IsNotNull(_settingsService.EditorSettings);

        bool newState = !_settingsService.EditorSettings.LeftPanelVisible;

        _settingsService.EditorSettings.LeftPanelVisible = newState;
        _settingsService.EditorSettings.RightPanelVisible = newState;
        _settingsService.EditorSettings.BottomPanelVisible = newState;
    }


    public ICommand ShowSettingsCommand => new AsyncRelayCommand(ShowSettings_Executed);
    private async Task ShowSettings_Executed()
    {
        await _dialogService.ShowSettingsDialogAsync();
    }
}
