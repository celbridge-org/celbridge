using Celbridge.Services;

namespace Celbridge.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly ISettingsService _settingsService;

        public SettingsViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService;

            Guard.IsNotNull(_settingsService.EditorSettings);

            ThemeIndex = (int)_settingsService.EditorSettings.ApplicationTheme;

            PropertyChanged += SettingsViewModel_PropertyChanged;
        }

        [ObservableProperty]
        private int _themeIndex;

        public List<string> ThemeValues { get; } = new() { "Light", "Dark" };

        private void SettingsViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ThemeIndex))
            {
                Guard.IsNotNull(_settingsService.EditorSettings);

                var theme = ThemeIndex == 0 ? ApplicationTheme.Light : ApplicationTheme.Dark;
                _settingsService.EditorSettings.ApplicationTheme = theme;
            }
        }
    }
}
