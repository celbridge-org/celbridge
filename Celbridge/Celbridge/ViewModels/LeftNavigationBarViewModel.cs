using Celbridge.Services;

namespace Celbridge.ViewModels
{
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
            _settingsService.EditorSettings.LeftPanelExpanded = !_settingsService.EditorSettings.LeftPanelExpanded;
        }

        public ICommand ToggleRightPanelCommand => new RelayCommand(ToggleRightPanel_Executed);
        private void ToggleRightPanel_Executed()
        {
            Guard.IsNotNull(_settingsService.EditorSettings);
            _settingsService.EditorSettings.RightPanelExpanded = !_settingsService.EditorSettings.RightPanelExpanded;
        }

        public ICommand ToggleBottomPanelCommand => new RelayCommand(ToggleBottomPanel_Executed);
        private void ToggleBottomPanel_Executed()
        {
            Guard.IsNotNull(_settingsService.EditorSettings);
            _settingsService.EditorSettings.BottomPanelExpanded = !_settingsService.EditorSettings.BottomPanelExpanded;
        }

        public ICommand ToggleAllPanelsCommand => new RelayCommand(ToggleAllPanels_Executed);
        private void ToggleAllPanels_Executed()
        {
            Guard.IsNotNull(_settingsService.EditorSettings);

            bool newState = !_settingsService.EditorSettings.LeftPanelExpanded;

            _settingsService.EditorSettings.LeftPanelExpanded = newState;
            _settingsService.EditorSettings.RightPanelExpanded = newState;
            _settingsService.EditorSettings.BottomPanelExpanded = newState;
        }


        public ICommand ShowSettingsCommand => new AsyncRelayCommand(ShowSettings_Executed);
        private async Task ShowSettings_Executed()
        {
            await _dialogService.ShowSettingsDialogAsync();
        }
    }
}
