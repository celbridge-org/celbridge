using Celbridge.Services;

namespace Celbridge.ViewModels
{
    public class SettingsViewModel : ObservableObject
    {
        private readonly ISettingsService _settingsService;

        public SettingsViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService;

            ThemeValues = Enum.GetValues(typeof(ApplicationTheme)).Cast<ApplicationTheme>();
        }

        public IEnumerable<ApplicationTheme> ThemeValues { get; }

        public ApplicationTheme Theme 
        { 
            get
            {
                Guard.IsNotNull(_settingsService.EditorSettings);
                return _settingsService.EditorSettings.ApplicationTheme;
            }
            set 
            {
                // Wrap model property
                Guard.IsNotNull(_settingsService.EditorSettings);
                SetProperty(_settingsService.EditorSettings.ApplicationTheme, 
                    value,    
                    _settingsService.EditorSettings,
                    (s, t) => s.ApplicationTheme = t);
            }
        }
    }
}
