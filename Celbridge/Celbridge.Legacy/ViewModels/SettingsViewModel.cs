using Celbridge.BaseLibrary.Settings;

namespace Celbridge.Legacy.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;

    public SettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;

        Guard.IsNotNull(_settingsService.EditorSettings);

        var themeName = "Dark";
        if (Enum.TryParse(themeName, out ApplicationColorTheme theme))
        {
            ThemeIndex = (int)theme;
        }

        PropertyChanged += SettingsViewModel_PropertyChanged;
    }

    [ObservableProperty]
    private int _themeIndex;

    public List<string> ThemeValues { get; } = new() { "Light", "Dark" };


    public string OpenAIKey
    {
        get
        {
            Guard.IsNotNull(_settingsService.EditorSettings);
            return _settingsService.EditorSettings.OpenAIKey;
        }

        set
        {
            Guard.IsNotNull(_settingsService.EditorSettings);
            _settingsService.EditorSettings.OpenAIKey = value;
            OnPropertyChanged(nameof(OpenAIKey));
        }
    }

    public string SheetsAPIKey
    {
        get
        {
            Guard.IsNotNull(_settingsService.EditorSettings);
            return _settingsService.EditorSettings.SheetsAPIKey;
        }

        set
        {
            Guard.IsNotNull(_settingsService.EditorSettings);
            _settingsService.EditorSettings.SheetsAPIKey = value;
            OnPropertyChanged(nameof(SheetsAPIKey));
        }
    }


    private void SettingsViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ThemeIndex))
        {
            Guard.IsNotNull(_settingsService.EditorSettings);

            var theme = ThemeIndex == 0 ? ApplicationColorTheme.Light : ApplicationColorTheme.Dark;

            //_settingsService.EditorSettings.Theme = theme.ToString();
        }
    }
}
