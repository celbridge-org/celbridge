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
        _settingsService.EditorSettings.IsLeftPanelVisible = !_settingsService.EditorSettings.IsLeftPanelVisible;
    }

    public ICommand ToggleRightPanelCommand => new RelayCommand(ToggleRightPanel_Executed);
    private void ToggleRightPanel_Executed()
    {
        Guard.IsNotNull(_settingsService.EditorSettings);
        _settingsService.EditorSettings.IsRightPanelVisible = !_settingsService.EditorSettings.IsRightPanelVisible;
    }

    public ICommand ToggleBottomPanelCommand => new RelayCommand(ToggleBottomPanel_Executed);
    private void ToggleBottomPanel_Executed()
    {
        Guard.IsNotNull(_settingsService.EditorSettings);
        _settingsService.EditorSettings.IsBottomPanelVisible = !_settingsService.EditorSettings.IsBottomPanelVisible;
    }

    public ICommand ToggleAllPanelsCommand => new RelayCommand(ToggleAllPanels_Executed);
    private void ToggleAllPanels_Executed()
    {
        Guard.IsNotNull(_settingsService.EditorSettings);

        bool newState = !_settingsService.EditorSettings.IsLeftPanelVisible;

        _settingsService.EditorSettings.IsLeftPanelVisible = newState;
        _settingsService.EditorSettings.IsRightPanelVisible = newState;
        _settingsService.EditorSettings.IsBottomPanelVisible = newState;
    }


    public ICommand ShowSettingsCommand => new AsyncRelayCommand(ShowSettings_Executed);
    private async Task ShowSettings_Executed()
    {
        await _dialogService.ShowSettingsDialogAsync();
    }
}
