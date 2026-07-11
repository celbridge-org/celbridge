using Celbridge.Settings;

namespace Celbridge.UserInterface.ViewModels.Pages;

/// <summary>
/// A selectable application colour theme paired with its localized display name, for the theme combo box.
/// </summary>
public sealed class ThemeOption
{
    public ThemeOption(ApplicationColorTheme theme, string displayName)
    {
        Theme = theme;
        DisplayName = displayName;
    }

    public ApplicationColorTheme Theme { get; }

    public string DisplayName { get; }
}

public partial class SettingsPageViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IUserInterfaceService _userInterfaceService;

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }

    [ObservableProperty]
    private ThemeOption? _selectedTheme;

    public SettingsPageViewModel(
        ISettingsService settingsService,
        IStringLocalizer stringLocalizer,
        IUserInterfaceService userInterfaceService)
    {
        _settingsService = settingsService;
        _stringLocalizer = stringLocalizer;
        _userInterfaceService = userInterfaceService;

        ThemeOptions = BuildThemeOptions();

        // Assign the backing field rather than the property so reflecting the stored theme in the combo box
        // does not run the changed handler, which would redundantly persist and re-apply the current theme.
        var storedTheme = _settingsService.Get(SettingCatalog.Application.Theme);
        _selectedTheme = ThemeOptions.FirstOrDefault(themeOption => themeOption.Theme == storedTheme);
    }

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (value is null)
        {
            return;
        }

        _settingsService.Set(SettingCatalog.Application.Theme, value.Theme);
        _userInterfaceService.ApplyCurrentTheme();
    }

    private List<ThemeOption> BuildThemeOptions()
    {
        var themeOptions = new List<ThemeOption>();

        var themeValues = Enum.GetValues(typeof(ApplicationColorTheme));
        foreach (ApplicationColorTheme theme in themeValues)
        {
            var stringKey = "Theme_" + Enum.GetName(typeof(ApplicationColorTheme), theme);
            var displayName = _stringLocalizer.GetString(stringKey);
            if (displayName is null)
            {
                throw new NotImplementedException("Cannot find localised string entry for '" + stringKey + "'");
            }

            var themeOption = new ThemeOption(theme, displayName);
            themeOptions.Add(themeOption);
        }

        return themeOptions;
    }
}
