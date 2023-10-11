using Celbridge.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using System.Threading.Tasks;
using System.Windows.Input;

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

        public ICommand ToggleProjectCommand => new RelayCommand(ToggleProject_Executed);
        private void ToggleProject_Executed()
        {
            // Toggle the project panel
            _settingsService.EditorSettings.LeftPanelExpanded = !_settingsService.EditorSettings.LeftPanelExpanded;
        }

        public ICommand ToggleConsoleCommand => new RelayCommand(ToggleConsole_Executed);
        private void ToggleConsole_Executed()
        {
            // Toggle the console panel
            _settingsService.EditorSettings.BottomPanelExpanded = !_settingsService.EditorSettings.BottomPanelExpanded;
        }

        public ICommand ShowSettingsCommand => new AsyncRelayCommand(ShowSettings_Executed);
        private async Task ShowSettings_Executed()
        {
            await _dialogService.ShowSettingsDialogAsync();
        }
    }
}
